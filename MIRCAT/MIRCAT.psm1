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
Class AdGpo {
    [string]$gpoName
    [string]$gpoGuid
    [bool]$isRisky
    [bool]$deploysSchedTask
    [bool]$deploysFiles
    [bool]$deploysSoftware
    [bool]$modsReg
    [bool]$modsLocalUG
    [bool]$modsEnvironment
    [System.Array]$riskReasons
    [string]$createdTime
    [string]$modifiedTime
    [System.Collections.Hashtable]$linkLocations #key=sompath, value=enabled
    AdGpo([string]$gpoName,[bool]$isRisky,[System.Array]$riskReasons,[string]$createdTime,[string]$modifiedTime,[System.Collections.Hashtable]$linkLocations)
    {
        $this.gpoName = $gpoName
        $this.isRisky = $isRisky
        $this.riskReasons = $riskReasons
        $this.createdTime = $createdTime
        $this.modifiedTime = $modifiedTime
        $this.linkLocations = $linkLocations
    }
    AdGpo([string]$gpoName)
    {
        $this.gpoName = $gpoName
    }
}
Class AdGpoRiskDetail {
    [string]$modifiedItem
    [string]$modifiedTarget
    [string]$modifiedSetting
    AdGpoRiskDetail(){}
    AdGpoRiskDetail([string]$modifiedItem){
        $this.modifiedItem = $modifiedItem
    }
    AdGpoRiskDetail([string]$modifiedItem,[string]$modifiedTarget,[string]$modifiedSetting){
        $this.modifiedItem = $modifiedItem
        $this.modifiedTarget = $modifiedTarget
        $this.modifiedSetting = $modifiedSetting
    }
}
Class AzureAdApp {
    [string]$displayName
    [string]$appId
    [string]$createdDateTime
    [string]$modifiedDateTime
    [bool]$isRisky
    [string]$lastModifiedBy
    [System.Collections.Hashtable]$riskyPermissions #key=permissionname, value=scope/role
    AzureAdApp(){
        $this.riskyPermissions = @{}
    }
    AzureAdApp([string]$displayName) {
        $this.displayName = $displayName
        $this.riskyPermissions = @{}
    }
    AzureAdApp([string]$displayName,[string]$appId) {
        $this.displayName = $displayName
        $this.appId = $appId
        $this.riskyPermissions = @{}
    }
    AzureAdApp([string]$displayName,[string]$appId,[datetime]$createdDateTime) {
        $this.displayName = $displayName
        $this.appId = $appId
        $this.createdDateTime = $createdDateTime
        $this.riskyPermissions = @{}
    }
    AzureAdApp([string]$displayName,[string]$appId,[bool]$isRisky) {
        $this.displayName = $displayName
        $this.appId = $appId
        $this.isRisky = $isRisky
        $this.riskyPermissions = @{}
    }
    AzureAdApp([string]$displayName,[string]$appId,[datetime]$createdDateTime,[bool]$isRisky) {
        $this.displayName = $displayName
        $this.appId = $appId
        $this.createdDateTime = $createdDateTime
        $this.isRisky = $isRisky
        $this.riskyPermissions = @{}
    }
    AzureAdApp([string]$displayName,[string]$appId,[bool]$isRisky,[System.Collections.Hashtable]$riskyPermissions) {
        $this.displayName = $displayName
        $this.appId = $appId
        $this.isRisky = $isRisky
        $this.riskyPermissions = @{}
        $this.riskyPermissions = $riskyPermissions
    }
    AzureAdApp([string]$displayName,[string]$appId,[datetime]$createdDateTime,[bool]$isRisky,[System.Collections.Hashtable]$riskyPermissions) {
        $this.displayName = $displayName
        $this.appId = $appId
        $this.createdDateTime = $createdDateTime
        $this.isRisky = $isRisky
        $this.riskyPermissions = @{}
        $this.riskyPermissions = $riskyPermissions
    }
}
Class AzureAdRole {
    [string]$objId
    [string]$displayName
    [System.Collections.Generic.List[AzureObject]]$objDetails
    AzureAdRole (){}
    AzureAdRole ([string]$objId)
    {
        $this.objId = $objId
    }
    AzureAdRole ([string]$objId,[string]$displayName)
    {
        $this.objId = $objId
        $this.displayName=$displayName
    }
    AzureAdRole ([string]$objId,[string]$displayName,[System.Collections.Generic.List[AzureObject]]$objDetails)
    {
        $this.objId = $objId
        $this.displayName=$displayName
        $this.objDetails=$objDetails
    }
}
Class AzureObject {
    [string]$objId
    [string]$objUpn
    [string]$displayName
    [string]$objType
    [string]$memberOf
    AzureObject (){}
    AzureObject ([string]$objId){
        $this.objId = $objId
    }
    AzureObject ([string]$objId,[string]$objUpn){
        $this.objId = $objId
        $this.objUpn = $objUpn
    }
    AzureObject ([string]$objId,[string]$objUpn,[string]$displayName,[string]$objType){
        $this.objId = $objId
        $this.objUpn = $objUpn
        $this.displayName = $displayName
        $this.objType = $objType
    }
    AzureObject ([string]$objId,[string]$objUpn,[string]$displayName,[string]$objType,[string]$memberOf){
        $this.objId = $objId
        $this.objUpn = $objUpn
        $this.displayName = $displayName
        $this.objType = $objType
        $this.memberOf = $memberOf
    }
}
class EntraConditionalAccessPolicy {
    
}
class EntraIdAl {
    [string]$tenantId
    [string]$tenantName
    [system.object]$aadSession
    [system.object]$graphSession
    hidden [System.Management.Automation.PSCredential]$credential
    EntraIdAl([string]$tenantId){
        #make sure you back fill the tenant name
        #$this.credential = $credential
        $this.tenantId = $tenantId
        try {
            $this.InitCloud()
            $this.graphSession = get-mgcontext
            $this.tenantName = (Get-MgDomain | Where-Object {$_.isInitial}).Id
        } catch {
            write-error "Failed to initialize cloud. $($_.Exception)"
        }
        
    }
    [void]InitCloud(){
        $this.InitMg()
    }
    hidden [void]InitAzure(){

    }
    hidden [void]InitMg(){
        try {
            if (!([bool](Get-InstalledModule "Microsoft.Graph.Identity.SignIns" -erroraction silentlycontinue))) {
                throw
            }
        } catch {
            Install-module Microsoft.Graph.Identity.SignIns -confirm:$false -force
        }
        try {
            if (!([bool](Get-InstalledModule "Microsoft.Graph.Identity.Governance" -erroraction silentlycontinue))) {
                throw
            }
        } catch {
            Install-module Microsoft.Graph.Identity.Governance -confirm:$false -force
        }
        try {
            if (!([bool](Get-InstalledModule "Microsoft.Graph.Authentication" -erroraction silentlycontinue))) {
                throw
            }
        } catch {
            Install-Module Microsoft.Graph.Authentication -confirm:$false -force
        }
        try {
            if (!([bool](Get-InstalledModule "Microsoft.Graph.Applications" -erroraction silentlycontinue))) {
                throw
            }
        } catch {
            Install-Module Microsoft.Graph.Applications -confirm:$false -force
        }
        try {
            if (!([bool](Get-InstalledModule "Microsoft.Graph.Identity.DirectoryManagement" -erroraction silentlycontinue))) {
                throw
            }
        } catch {
            Install-Module Microsoft.Graph.Identity.DirectoryManagement -confirm:$false -force
        }
        #Microsoft.Graph.DirectoryObjects
        try {
            if (!([bool](Get-InstalledModule "Microsoft.Graph.DirectoryObjects" -erroraction silentlycontinue))) {
                throw
            }
        } catch {
            Install-Module Microsoft.Graph.DirectoryObjects -confirm:$false -force
        }
        try {
            if (!([bool](Get-InstalledModule "Microsoft.Graph.Reports" -erroraction silentlycontinue))) {
                throw
            }
        } catch {
            Install-Module Microsoft.Graph.Reports -confirm:$false -force
        }
        #Microsoft.Graph.Users
        try {
            if (!([bool](Get-InstalledModule "Microsoft.Graph.Users" -erroraction silentlycontinue))) {
                throw
            }
        } catch {
            Install-Module Microsoft.Graph.Users -confirm:$false -force
        }
        try {
            if (!([bool](Get-InstalledModule "Microsoft.Graph.Groups" -erroraction silentlycontinue))) {
                throw
            }
        } catch {
            Install-Module Microsoft.Graph.Groups -confirm:$false -force
        }
        import-module Microsoft.Graph.Authentication -force
        import-module Microsoft.Graph.Applications -force
        import-module Microsoft.Graph.Identity.SignIns -force
        import-module Microsoft.Graph.Identity.DirectoryManagement -force
        import-module Microsoft.Graph.DirectoryObjects -force
        import-module Microsoft.Graph.Identity.Governance -force
        import-module Microsoft.Graph.Reports -force
        import-module Microsoft.Graph.Users -force
        import-module Microsoft.Graph.Groups -force
        if (!(get-mgcontext)) {
            connect-MgGraph -Scopes "Policy.ReadWrite.ConditionalAccess","Group.ReadWrite.All","AuditLog.Read.All","Directory.Read.All","Directory.ReadWrite.All","RoleManagement.Read.Directory","RoleManagement.ReadWrite.Directory","Policy.Read.All","Group.Read.All","Group.ReadWrite.All","GroupMember.Read.All","DeviceManagementApps.Read.All","DeviceManagementApps.ReadWrite.All","DeviceManagementConfiguration.Read.All","DeviceManagementConfiguration.ReadWrite.All","DeviceManagementManagedDevices.Read.All","DeviceManagementManagedDevices.ReadWrite.All","DeviceManagementServiceConfig.Read.All","DeviceManagementServiceConfig.ReadWrite.All","User.Read.All","User.ReadBasic.All","User.ReadWrite.All","Application.Read.All","Application.ReadWrite.All","RoleManagement.Read.Directory","RoleManagement.ReadWrite.Directory" -TenantId $this.tenantId
        }
    }
    [void]DisposeMg(){
        if (get-mgcontext) {
            disconnect-MgGraph
        }
    }
    [bool]NewUser([string]$displayName,[securestring]$password,[string]$mailNickName,[string]$userPrincipalName,[System.Collections.Hashtable]$bodyParams){
        $returnVal = $null
        $userCheck = $null
        $passwordProfile = @{
            Password = $password
        }
        if ($displayName) { 
            try {
                $userCheck = new-mguser -DisplayName $displayName -PasswordProfile $passwordProfile -AccountEnabled -MailNickName $mailNickName -UserPrincipalName $userPrincipalName
            } catch {
                write-error "NewUser $($_.Exception)"
            }
        } elseif ($bodyParams) {
            try {
                $userCheck = new-mguser -BodyParameter $bodyParams
            } catch {
                write-error "NewUser body params $($_.Exception)"
            }
        }
        if ($userCheck) {
            $returnVal = $true
        }
       return $returnVal
    }
    [bool]SetUser([string]$userId,[System.Collections.Hashtable]$bodyParams){
        $returnVal = $false
        $setCheck = $null
        try {
            $setCheck = Update-MgUser -userid $userId -BodyParameter $bodyParams
            $returnVal = $true
        } catch {
            $returnVal = $false
            write-error "SetUser $($_.Exception)"
        }
        return $returnVal
    }
    [system.object]GetUser([string]$userId,[string]$filter){
        $returnVal = $null
        $user = $null
        if ($userId) {
            try {
                $user = get-mguser -userId "$userId"
            } catch {
                write-error "GetUser userId $($_.Exception)"
            }
        } elseif ($filter) {
            try {
                $filterString = '{0}' -f $filter
                $user = get-mguser -filter $filterString
            } catch {
                write-error "GetUser filter $($_.Exception)"
            }
        }
        if ($user) {
            $returnVal = $user
        }
        return $returnVal
    }
    [bool]RemoveUser([string]$userId){
        $returnVal = $false
        $removeCheck = $null
        try {
            $removeCheck = Remove-MgUser -UserId $userId
            $returnVal = $true
        } catch {
            $returnVal = $false
            write-error "RemoveUser $($_.Exception)"
        }
        return $returnVal
    }
    [system.object]GetGroup([string]$groupId,[string]$filter){
        $returnVal = $null
        $group = $null
        if ($groupId) {
            try {
                $group = get-mggroup -groupId "$groupId"
            } catch {
                write-error "GetGroup groupId $($_.Exception)"
            }
        } elseif ($filter) {
            try {
                $filterString = '{0}' -f $filter
                $group = get-mggroup -filter $filterString
            } catch {
                write-error "GetGroup filter $($_.Exception)"
            }
        }
        if ($group) {
            $returnVal = $group
        }
        return $returnVal
    }
    [bool]NewGroup([string]$displayName,[string]$mailNickName,[bool]$mailEnabled,[bool]$securityEnabled,[bool]$dynamicEnabled,[string]$membershipRule,[string]$membershipRuleProcessingState,[System.Collections.Hashtable]$bodyParams){
        $returnVal = $false
        $groupCheck = $null
        if ($displayName) { 
            try {
                if ($dynamicEnabled){
                    $groupCheck = new-mggroup -DisplayName $displayName -MailEnabled:$mailEnabled -MailNickName $mailNickName -securityEnabled:$securityEnabled -dynamicEnabled -MembershipRule $membershipRule -MembershipRuleProcessingState $membershipRuleProcessingState
                } else {
                    $groupCheck = new-mggroup -DisplayName $displayName -MailEnabled:$mailEnabled -MailNickName $mailNickName -securityEnabled:$securityEnabled
                }
                
            } catch {
                write-error "NewGroup $($_.Exception)"
            }
        } elseif ($bodyParams) {
            try {
                $groupCheck = new-mggroup -BodyParameter $bodyParams
            } catch {
                write-error "NewGroup body params $($_.Exception)"
            }
        }
        if ($groupCheck) {
            $returnVal = $true
        }
        return $returnVal
    }
    [bool]SetGroup([System.Collections.Hashtable]$bodyParams){
        $returnVal = $false
        $setCheck = $null
        try {
            $setCheck = Update-mggroup -BodyParameter $bodyParams
            $returnVal = $true
        } catch {
            $returnVal = $false
            write-error "SetGroup $($_.Exception)"
        }
        return $returnVal
    }
    [bool]RemoveGroup([string]$groupId){
        $returnVal = $false
        $removeCheck = $null
        try {
            $removeCheck = Remove-MgGroup -groupid $groupId
            $returnVal = $true
        } catch {
            $returnVal = $false
            write-error "RemoveGroup $($_.Exception)"
        }
        return $returnVal
    }
    [bool]AddUserToGroup([string]$groupId,[string]$userId){
        $returnVal = $false
        $addCheck = $null
        try {
            $addCheck = New-MgGroupMember -GroupId $groupId -DirectoryObjectId $userId
            $returnVal = $true
        } catch {
            $returnVal = $false
            write-error "AddUserToGroup $($_.Exception)"
        }
        return $returnVal
    }
    [bool]RemoveUserFromGroup([string]$groupId,[string]$userId){
        $returnVal = $false

        return $returnVal
    }
    [system.object]GetObject([string[]]$objId,[string[]]$types){
        $returnVal = $null
        try {
            $returnVal = Get-MgDirectoryObjectById -Ids $objId -Types $types
        } catch {
            write-error "GetObject $($_.Exception)"
        }
        return $returnVal
    }
    [system.object]NewApplication([string]$displayName,[System.Collections.Hashtable]$bodyParams){
        $returnVal = $null
        if ($displayName){
            try {
                $returnVal = New-MgApplication -displayname $displayName
            } catch {
                write-error "NewApplication $($_.Exception)"
            }
        } elseif ($bodyParams) {
            try {
                $returnVal = New-MgApplication -BodyParameter $bodyParams
            } catch {
                write-error "NewApplication $($_.Exception)"
            }
        }
        return $returnVal
    }
    [system.object]GetAppliction([string]$appId,[System.Collections.Hashtable]$bodyParams){
        $returnVal = $null
        if ($appId) {
            try {
                $returnVal = Get-MgApplication -ApplicationId $appId
            } catch {
                write-error "GetApplication $($_.Exception)"
            }
        } elseif ($bodyParams) {
            try {
                $returnVal = Get-MgApplication -InputObject $bodyParams
            } catch {
                write-error "GetApplication body params $($_.Exception)"
            }
        }
        return $returnVal
    }
    [bool]RemoveApplication([string]$appId){
        $returnVal = $false
        $removeCheck = $false
        try {
            $removeCheck = Remove-MgApplication -ApplicationId $appId
            $returnVal = $true
        } catch {
            $returnVal = $false
            write-error "RemoveApplication $($_.Exception)"
        }
        return $returnVal
    }
    [bool]SetApplication([string]$appId,[System.Collections.Hashtable]$bodyParams,[System.Collections.Hashtable]$keyCreds){
        $returnVal = $false
        if ($bodyParams) {
            try {
                $setCheck = Update-MgApplication -ApplicationId $appId -BodyParameter $bodyParams
                $returnVal = $true
            } catch {
                $returnVal = $false
                write-error "SetApplication $($_.Exception)"
            }
        } elseif ($keyCreds) {
            try {
                $setCheck = Update-MgApplication -ApplicationId $appId -KeyCredentials $keyCreds
                $returnVal = $true
            } catch {
                $returnVal = $false
                write-error "SetApplication key creds $($_.Exception)"
            }
        }
        
        return $returnVal
    }
    [bool]NewNamedLocation([System.Collections.Hashtable]$bodyParams){
        $returnVal = $false
        $newCheck = $null
        try {
            $newCheck = New-MgIdentityConditionalAccessNamedLocation -BodyParameter $bodyParams
            $returnVal = $true
        } catch {
            $returnVal = $false
            write-error "NewNamedLocation $($_.Exception)"
        }
        return $returnVal
    }
    [system.object]GetNamedLocation([string]$nlId,[string]$nlName){
        $returnVal = $null
        $nl = $null
        if ($nlId) {
            try {
                $nl = Get-MgIdentityConditionalAccessNamedLocation -NamedLocationId $nlId
            } catch {
                $nl = $null
                write-error "GetNamedLocation by id $($_.Exception)"
            }
        } elseif ($nlName) {
            try {
                $nl = Get-MgIdentityConditionalAccessNamedLocation | ? { $_.DisplayName -eq "$nlName"}
            } catch {
                $nl = $null
                write-error "GetNamedLocation by name $($_.Exception)"
            }
        }
        $returnVal = $nl
        return $returnVal
    }
    [bool]RemoveNamedLocation([string]$nlId){
        $returnVal = $false
        $removecheck = $null
        try {
            $removeCheck = Remove-MgIdentityConditionalAccessNamedLocation -NamedLocationId $nlId
            $returnVal = $true
        } catch {
            $returnVal = $false
            write-error "RemoveNamedLocation $($_.Exception)"
        }
        return $returnVal
    }
    [bool]SetNamedLocation([string]$nlId,[System.Collections.Hashtable]$bodyParams){
        $returnVal = $false
        #Update-MgIdentityConditionalAccessNamedLocation -NamedLocationId <String> -BodyParameter <IMicrosoftGraphNamedLocation>
        $setCheck = $false
        try {
            $setCheck = Update-MgIdentityConditionalAccessNamedLocation -NamedLocationId $nlId -BodyParameter $bodyParams
            $returnVal = $true
        } catch {
            $returnVal = $false
            write-error "SetNamedLocation $($_.Exception)"
        }
        return $returnVal
    }
    [bool]NewCaPol([System.Collections.Hashtable]$bodyParams){
        $returnVal = $false
        $newCheck = $null
        try {
            $newCheck = New-MgIdentityConditionalAccessPolicy -BodyParameter $bodyParams
            $returnVal = $true
        } catch {
            $returnVal = $false
            write-error "NewCaPol $($_.Exception)"
        }
        return $returnVal
    }
    [system.object]GetCaPol([string]$caId,[string]$caName){
        $returnVal = $null
        $ca = $null
        if ($caId) {
            try {
                $ca = Get-MgIdentityConditionalAccessPolicy -ConditionalAccessPolicyId $caId
            } catch {
                $ca = $null
                write-error "GetCaPol by id $($_.Exception)"
            }
        } elseif ($caName) {
            try {
                $ca = Get-MgIdentityConditionalAccessPolicy | ? { $_.DisplayName -eq "$caName"}
            } catch {
                $ca = $null
                write-error "GetCaPol by name $($_.Exception)"
            }
        }
        $returnVal = $ca
        return $returnVal
    }
    [bool]RemoveCaPol([string]$caId){
        $returnVal = $false
        $removecheck = $null
        try {
            $removeCheck = Remove-MgIdentityConditionalAccessPolicy -ConditionalAccessPolicyId $caId
            $returnVal = $true
        } catch {
            $returnVal = $false
            write-error "RemoveCaPol $($_.Exception)"
        }
        return $returnVal
    }
    [bool]SetCaPol([string]$caId,[System.Collections.Hashtable]$bodyParams){
        $returnVal = $false
        $setCheck = $false
        try {
            $setCheck = Update-MgIdentityConditionalAccessPolicy -NamedLocationId $caId -BodyParameter $bodyParams
            $returnVal = $true
        } catch {
            $returnVal = $false
            write-error "SetCaPol $($_.Exception)"
        }
        return $returnVal
    }
    [bool]NewIntuneScopeTag([string]$uri,[string]$displayName,[string]$description,[System.Collections.Hashtable]$bodyParams){
        $returnVal = $false
        $newCheck = $null
        [System.Collections.Hashtable]$builtParams=@{}
        try {
            if (!($uri)){
                $uri = "https://graph.microsoft.com/beta/deviceManagement/roleScopeTags"
            }
            if (!($bodyParams)) {
                if (!($displayName)) {
                    throw "If no body params, you need a displayName"
                }
                $builtParams.add('@odata.type','#microsoft.graph.roleScopeTag')
                $builtParams.add("isBuiltIn",$false)
                $builtParams.add("description","$description")
                $builtParams.add("displayName","$displayName")
                
            } else {
                $builtParams = $bodyParams
            }
            $builtParams = $builtParams | convertto-json
            $newCheck = Invoke-MgGraphRequest -Method POST -Uri "$uri" -body $builtParams -ContentType 'application/json'
            $returnVal = $true
        } catch {
            $returnVal = $false
            write-error "NewIntuneScopeTag $($_.Exception)"
        }
        
        return $returnVal
    }
    [system.object]GetIntuneScopeTag([string]$uri,[string]$displayName){
        $returnVal = $null
        
        try {
            if (!($uri)){
                $uri = "https://graph.microsoft.com/beta/deviceManagement/roleScopeTags"
            }
            if (!($displayName)) {
                throw "You need a displayName"
            } else {
                $uri = $uri+"`?`$filter=displayName eq '$DisplayName'"
                $returnVal = Invoke-MgGraphRequest -Method GET -Uri $uri
            }
        } catch {
            $returnVal = $null
            write-error "GetIntuneScopeTag $($_.Exception)"
        }
        return $returnVal
    }
    [bool]RemoveIntuneScopeTag([string]$uri,[string]$scopeTagId,[System.Collections.Hashtable]$bodyParams){
        $returnVal = $false
        $removeCheck = $null
        [System.Collections.Hashtable]$builtParams=@{}
        try {
            if (!($uri)){
                $uri = "https://graph.microsoft.com/beta/deviceManagement/roleScopeTags"
            }
            $uri = $uri+"/$scopeTagId"
            $removeCheck = Invoke-MgGraphRequest -Method Delete -Uri $uri
            $returnVal = $true
        } catch {
            $returnVal = $false
            write-error "RemoveIntuneScopeTag $($_.Exception)"
        }
        
        return $returnVal
    }
    [bool]AssignIntuneScopeTagToGroup([string]$uri,[string]$scopeTagId,[string]$groupId,[System.Collections.Hashtable]$bodyParams){
        $returnVal = $false
        $assignCheck = $null
        [System.Collections.Hashtable]$builtParams=@{}
        try {
            if (!($uri)){
                $uri = "https://graph.microsoft.com/beta/deviceManagement/roleScopeTags"
            }
            $uri = $uri+"/$scopeTagId/assignments"
            if (!($bodyParams)) {
                $builtParams.add('@odata.type','#microsoft.graph.groupAssignmentTarget')
                $builtParams.add("groupId","$groupId")
            } else {
                $builtParams = $bodyParams
            }
            $builtParams = $builtParams | convertto-json
            $assignCheck = Invoke-MgGraphRequest -Method POST -Uri "$uri" -body $builtParams -ContentType 'application/json'
            $returnVal = $true
        } catch {
            $returnVal = $false
            write-error "AssignIntuneScopeTagToGroup $($_.Exception)"
        }
        return $returnVal
    }
    [system.object]GetIntuneDeviceComplianceProfile([string]$uri,[string]$profileName){
        $returnVal = $null
        try {
            if (!($uri)) {
                $uri = "https://graph.microsoft.com/beta/deviceManagement/deviceCompliancePolicies"
            }
            $returnVal = Invoke-MgGraphRequest -Method Get -Uri $uri | ? { ($_.'displayName') -eq "$profileName" }
        } catch {
            $returnVal = $null
            write-error "GetIntuneDeviceComplianceProfile $($_.Exception)"
        }
        return $returnVal
    }
    [bool]NewIntuneDeviceComplianceProfile([string]$uri,[IntuneDeviceComplianceProfile]$profile){
        $returnVal = $false
        $jsonProfile = $null
        try {
            if (!($uri)) {
                $uri = "https://graph.microsoft.com/beta/deviceManagement/deviceCompliancePolicies"
            }
            $jsonProfile = $profile | convertto-json
            Invoke-MgGraphRequest -Method Post -Uri $uri -Body $jsonProfile -ContentType 'application/json'
            $returnVal = $true
        } catch {
            $returnVal = $false
            write-error "NewIntuneDeviceComplianceProfile $($_.Exception)"
        }
        return $returnVal
    }
    [bool]AssignIntuneDeviceComplianceProfile([string]$uri,[string]$id,[IntuneDeviceComplianceAssignment[]]$assignments){
        $returnVal = $false
        $jsonBody = $null
        if (!($uri)) {
            $uri = "https://graph.microsoft.com/v1.0/deviceManagement/deviceCompliancePolicies/$id/assign"
        }
        try {
            $jsonBody = @{
                'Assignments' = $assignments
            } | convertto-json -depth 5
            Invoke-MgGraphRequest -Method Post -Uri $uri -Body $jsonBody -ContentType 'application/json'
            $returnVal = $true
        } catch {
            $returnVal = $false
            write-error "AssignIntuneDeviceComplianceProfile $($_.Exception)"
        }
        return $returnVal
    }
    [bool]RemoveIntuneDeviceComplianceProfile([string]$uri,[string]$id){
        $returnVal = $false
        if (!($uri)) {
            $uri = "https://graph.microsoft.com/beta/deviceManagement/deviceCompliancePolicies"
        }
        try {
            Invoke-MgGraphRequest -Method Delete -Uri $($uri+"/$id")
            $returnVal = $true
        } catch {
            $returnVal = $false
            write-error "RemoveIntuneDeviceComplianceProfile $($_.Exception)"
        }
        return $returnVal
    }
    [bool]NewIntuneRbacRole([string]$uri,[IntuneRbacRole]$rbacRole){
        $returnVal = $false
        $jsonBody = $null
        if (!($uri)) {
            $uri = "https://graph.microsoft.com/v1.0/deviceManagement/roleDefinitions"
        }
        try {
            $jsonBody = $rbacRole | convertto-json -depth 5
            Invoke-MgGraphRequest -Method Post -Uri $uri -Body $jsonBody -ContentType 'application/json'
            $returnVal = $true
        } catch {
            $returnVal = $false
            write-error "NewIntuneRbacRole $($_.Exception)"
        }
        return $returnVal
    }
}
class graphAL_auth
{
  [object]$tokenResponse
  [string]$accessToken
  [string]$refreshToken
  [string]$tenantId
  [string]$userId
  [string]$codeVerifier
  [string]$codeChallenge

  graphAL_auth()
  {

    if ($null -eq $this.accessToken)
    {
      $this.authenticator("common")
    } else {
      $this.tokenValidator($this.accessToken)
    }

    $this.getAuthDetails($this.accessToken)
  }

  #region authenticator
  [void]authenticator([string]$tenantid)
  {
    # Function to start a local HTTP listener to capture the authorization code
    function Start-HttpListener 
    {
      param (
        [int]$port = 8080
      )

      $listener = New-Object System.Net.HttpListener
      $listener.Prefixes.Add("http://localhost:$port/")
      $listener.Start()
      Write-Host "graphAL_Auth: Listening for authorization code on port $port..."

      $context = $listener.GetContext()
      $request = $context.Request
      $response = $context.Response

      $authCode = $request.QueryString["code"]

      $responseString = "<html><body>Authentication successful. You can close this window.</body></html>"
      $buffer = [System.Text.Encoding]::UTF8.GetBytes($responseString)
      $response.ContentLength64 = $buffer.Length
      $response.OutputStream.Write($buffer, 0, $buffer.Length)
      $response.OutputStream.Close()

      $listener.Stop()

      return $authCode
    }
    #endregion authenticator

    #region OAuthCodeFlowWithPKCE
    function Invoke-OAuthCodeFlowWithPKCE 
    {
      param (
        [string]$tenantid
      )

      # Generate a code verifier and code challenge
      $this.codeVerifier = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([guid]::NewGuid().ToString())).TrimEnd('=').Replace('+', '-').Replace('/', '_')
      $this.codeChallenge = [System.Convert]::ToBase64String([System.Security.Cryptography.SHA256]::Create().ComputeHash([System.Text.Encoding]::UTF8.GetBytes($this.codeVerifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_')

      # Define the login URL
      $loginUrl = "https://login.microsoftonline.com/$tenantid/oauth2/v2.0/authorize"
      
      # Define the parameters for the login request
      $params = @{
        client_id     = "1950a258-227b-4e31-a9cf-717495945fc2" # Global Powershell AppId
        response_type = "code"
        redirect_uri  = "http://localhost:8080"
        response_mode = "query"
        scope         = "openid profile offline_access https://graph.microsoft.com/Directory.AccessAsUser.All"
        state         = [guid]::NewGuid().ToString()
        code_challenge = $this.codeChallenge
        code_challenge_method = "S256"
      }

      # Open the login URL in the default web browser
      $url = $loginUrl + "?" + (($params.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join "&")
      Start-Process $url

      # Start the HTTP listener to capture the authorization code
      $authCode = Start-HttpListener -port 8080

      # Exchange the authorization code for an access token
      $tokenUrl = "https://login.microsoftonline.com/$tenantid/oauth2/v2.0/token"
      $tokenParams = @{
        client_id     = $params.client_id
        grant_type    = "authorization_code"
        code          = $authCode
        redirect_uri  = $params.redirect_uri
        code_verifier = $this.codeVerifier
      }

      $response = Invoke-WebRequest -Uri $tokenUrl -Method Post -ContentType "application/x-www-form-urlencoded" -Body $tokenParams
      $tokenResponse = $response.Content | ConvertFrom-Json

      return $tokenResponse
    }

    $this.tokenResponse = Invoke-OAuthCodeFlowWithPKCE -tenantid $tenantid
    $this.accessToken = $this.tokenResponse.access_token
    $this.refreshToken = $this.tokenResponse.refresh_token
  }
  #endregion OAuthCodeFlowWithPKCE

  #region tokenRefresh
  [bool]tokenRefresh([string]$tenantid,[string]$refreshToken)
  {
    function Invoke-OAuthCodeFlowWithRefreshTokenAndPKCE 
    {
      param (
        [string]$tenantid,
        [string]$refreshToken
      )

      # Generate a code verifier and code challenge
      #$codeVerifier = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([guid]::NewGuid().ToString())).TrimEnd('=').Replace('+', '-').Replace('/', '_')
      #$codeChallenge = [System.Convert]::ToBase64String([System.Security.Cryptography.SHA256]::Create().ComputeHash([System.Text.Encoding]::UTF8.GetBytes($codeVerifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_')

      # Define the token URL
      $tokenUrl = "https://login.microsoftonline.com/$tenantid/oauth2/v2.0/token"
      Write-Host $tokenUrl -ForegroundColor Magenta

      # Define the parameters for the token request
      $params = @{
        client_id     = "1950a258-227b-4e31-a9cf-717495945fc2" # Global Powershell AppId
        grant_type    = "refresh_token"
        refresh_token = $refreshToken
        code_verifier = $this.codeVerifier
        scope         = "openid profile offline_access https://graph.microsoft.com/Directory.AccessAsUser.All"
      }

      # Request a new access token using the refresh token
      $response = Invoke-WebRequest -Uri $tokenUrl -Method Post -ContentType "application/x-www-form-urlencoded" -Body $params
      $tokenResponse = $response.Content | ConvertFrom-Json

      return $tokenResponse
    }

    try {
      $this.tokenResponse =  Invoke-OAuthCodeFlowWithRefreshTokenAndPKCE -tenantid $tenantid -refreshToken $refreshToken
      $this.accessToken = $this.tokenResponse.access_token
      $this.refreshToken = $this.tokenResponse.refresh_token
      return $true
    } catch {
      Write-Error "graphAL_Auth: Failed to refresh token: $_"
      return $false
    }

  }
  #endregion tokenRefresh

  #region tokenValidator
  # This function checks the token's expiration and refreshes it if necessary
  [bool]tokenValidator([string]$token)
  {
    try {
      $tokenParts = $token -split '\.'
      if ($tokenParts.Length -ne 3) {
        throw "graphAL_Auth: Invalid JWT token format"
      }

      $header = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($tokenParts[0]))
      $payload = $tokenParts[1]
      switch ($payload.Length % 4) {
        2 { $payload += '==' }
        3 { $payload += '=' }
      }
      $payload = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($payload))
      $signature = $tokenParts[2]

      $jwtHeader = $header | ConvertFrom-Json
      $jwtPayload = $payload | ConvertFrom-Json

      # Calculate current epoch time using DateTimeOffset for timezone-independent result
      $currentEpochTime = [int][DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
      if ($currentEpochTime -ge $jwtPayload.exp)
      {
        Write-Host "graphAL_Auth: Token has expired" -ForegroundColor Magenta
        if ($null -ne $this.refreshToken) 
        {
          try {
            write-host "graphAL_Auth: Refreshing token" -ForegroundColor Magenta
            $This.tokenRefresh($jwtPayload.tid,$this.refreshToken)
          } catch {
            Write-Host "graphAL_Auth: Failed to refresh token" -ForegroundColor Red
          }
        } Else {
          Write-Host "graphAL_Auth: re-authenticating" -ForegroundColor Magenta
          $This.authenticator("common")
        }
      } Else {
        $expirationDate = [System.DateTimeOffset]::FromUnixTimeSeconds($jwtPayload.exp).DateTime
        #Write-Host "Token is valid until: $expirationDate" -ForegroundColor Magenta
      }

      return $true
    } catch {
      Write-Error "graphAL_Auth: Token validation failed: $_"
      return $false
    }
  }
  #endregion tokenValidator

  #region getAuthDetails
  # This function extracts the tenant ID and user ID from the JWT token
  [void]getAuthDetails($token)
  {
    try {
      $tokenParts = $token -split '\.'
      if ($tokenParts.Length -ne 3) {
        throw "graphAL_Auth: Invalid JWT token format"
      }

      $header = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($tokenParts[0]))
      $payload = $tokenParts[1]
      switch ($payload.Length % 4) {
        2 { $payload += '==' }
        3 { $payload += '=' }
      }
      $payload = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($payload))
      $signature = $tokenParts[2]

      $jwtHeader = $header | ConvertFrom-Json
      $jwtPayload = $payload | ConvertFrom-Json

      $this.tenantId = $jwtPayload.tid
      $this.userId = $jwtPayload.upn

    } catch {
      Write-Error "graphAL_Auth: Token validation failed: $_"
    }

  }
  #endregion getAuthDetails
  
  #region getAccessToken
  # This function returns the access token if it is valid, otherwise it refreshes the token
  [string]getAccessToken()
  {
    $this.tokenValidator($this.accessToken)
    return $this.accessToken
  }
  #endregion getAccessToken
}
class GraphAL
{
  [string] $uri = "https://graph.microsoft.com"
  [string] $apiVersion = "v1.0"
  [string] $currentObjectType
  [string] $objectId
  [string] $filter
  [string] $objectProperties
  [hashtable] $postBody
  [hashtable] $objectTypeList = [graphObjectTypePermissions]::new().getAll()

  #region init
  GraphAL([hashtable]$properties = @{})
  {
    if ($null -eq $global:GraphALContext)
    {
      $Global:GraphALContext = [graphAL_auth]::new()
    } Else {
      Write-host "Connected to tenant: " $Global:GraphALContext.tenantId -ForegroundColor Magenta
      Write-host "Connected as user: " $Global:GraphALContext.userId -ForegroundColor Magenta
    }

    if ($properties.Count -gt 0) 
    {
      $this.init($properties)
    } Else {
      [string]$this.uri = $this.uri,$this.apiVersion -join "/"
    }
  }

  #GraphAL([hashtable]$properties) {$this.init($properties)}

  [void]init($properties)
  {

    [array]$uriConstructor = @()
    $uriConstructor += $this.uri

    if(![string]::IsNullOrEmpty($properties.apiVersion)){$this.apiversion = $properties.apiVersion}
    $uriconstructor += $this.apiversion

    if(![string]::IsNullOrEmpty($properties.objecttype))
    {
      $this.currentObjectType = $properties.objectType

      If ($this.objectTypeList.Keys -notcontains $properties.objectType)
      {
        Write-Error "Object type not supported: $($properties.objectType)"
        Write-Host "Supported object types: $($this.objectTypeList.Keys -join ", ")" -ForegroundColor Red
        exit
      }

      $uriConstructor += $this.objectTypeList.item($properties.objectType).apiRef
    }

    $this.uri =  $uriConstructor -join "/"

  }

  #endregion init

  #region invoke
  [array]invoke([hashtable]$properties)
  {
    $invokeuri = $this.uri # setting this to avoid modifying the uri in the instance of the class
    
    if ($properties.uri[0] -eq "?")
    {
      $invokeuri = $this.uri,$properties.uri -join ""      
    } Else {
      $invokeuri = $invokeuri,$properties.uri -join "/"
    }
    #$invokeuri = $invokeuri,$properties.uri -join "/"
    
    $params = @{}
    $params.Add("Method",$properties.Method)
    $params.Add("Uri",$invokeuri)
    $params.Add("Body",($properties.Body | ConvertTo-Json -Depth 10))

    function new-header 
    {
      $at = $Global:GraphALContext.getAccessToken()

      $headers = @{
        'Authorization'         = 'Bearer ' + $at
        'Content-Type'          = 'application/json'
        'X-Requested-With'      = 'XMLHttpRequest'
        'x-ms-client-request-id'= [guid]::NewGuid()
        'x-ms-correlation-id'   = [guid]::NewGuid()
      }

      return $headers
    }

    [hashtable]$headers = new-header

    If ($properties.Headers)
    {
      $headers += $properties.Headers
    }

    $params.Add("Headers",$Headers)

    $returnVal = @()

    write-host $($properties.method) $invokeuri -ForegroundColor Magenta

    $graphResults = Invoke-RestMethod @params
    $returnVal += $graphResults

    if ($graphResults.'@odata.nextLink')
    {
      do {
        $graphResults = (Invoke-RestMethod -Uri $graphResults.'@odata.nextLink' -headers (new-header))
        $returnVal += $graphResults
      } until (
        -not($graphResults.'@odata.nextLink')
      )
    }

    return $returnval
  }
  #endregion invoke

  #region get
  [array] Get([hashtable]$properties = @{})
  {

    If (![string]::IsNullOrEmpty($properties.objectId) -and ![string]::IsNullOrEmpty($properties.filter))
    {
      Write-Error "Cannot use both objectId and filter at the same time"
      exit
    }

    # Had to put the uri in a separate variable to avoid the uri in the instance of the class to be modified.
    [string]$getUri = ""

    # Query for a specific object
    if (![string]::IsNullOrEmpty($properties.objectId))
    {
      #$getUri = $getUri,$properties.objectId -join "/"
      $getUri = $properties.objectId -join "/"
    }

    # Append filter to the uri
    if (![string]::IsNullOrEmpty($properties.filter))
    {
      #$getUri = $getUri,"?`$filter=",$properties.filter -join ""
      $getUri = "?`$filter=",$properties.filter -join ""
    }

    If (![string]::IsNullOrEmpty($properties.objectProperties))
    {
      #$getUri = $getUri,"?`$select=",$properties.objectProperties -join ""
      $getUri = "?`$select=",$properties.objectProperties -join ""
    }

    If (![string]::IsNullOrEmpty($properties.additionalParameters))
    {
      <#
        This must be populated with full query string that should be appended to the rest call. 
        For example: '&`$expand=principal"
        It was needed for the derived class entraRoleManagement to list members of an assignment.
        I figured it would be better to define it here rather than adding the whole method to the derived class.
        Also it gives some flexibility for other dervived classes.
      #>

      #$getUri = $getUri,$properties.additionalParameters -join ""
      $getUri = $properties.additionalParameters -join ""
    }

    $graphResults = $this.invoke(@{
      Method = "GET"
      Uri = $getUri
    })

    Return $graphResults
  }
  #endregion get


  #region post
  [void] Create([hashtable]$properties)
  {
    [string]$postUri = ""

    # Target a specific object
    if (![string]::IsNullOrEmpty($properties.objectId))
    {
      $postUri = $postUri,$properties.objectId -join "/"
    }

    If (![string]::IsNullOrEmpty($properties.additionalParameters))
    {
      <#
        This must be populated with full query string that should be appended to the rest call. 
        For example: '&`$expand=principal"
        It was needed for the derived class entraRoleManagement to list members of an assignment.
        I figured it would be better to define it here rather than adding the whole method to the derived class.
        Also it gives some flexibility for other dervived classes.
      #>

      $postUri = $postUri,$properties.additionalParameters -join ""
    }

    if ($null -ne $properties.postBody)
    {
      write-host "Body" $($properties.postBody) -ForegroundColor Magenta
  
      $this.invoke(@{
        Method = "POST"
        Uri = $postUri
        Body = ($properties.postBody)
      })
    } else {
      $this.invoke(@{
        Method = "POST"
        Uri = $postUri
      })
    }
  }
  
  #endregion post

  #region patch
  [void] Update([hashtable]$properties)
  {
    # Had to put the uri in a separate variable to avoid the uri in the instance of the class to be modified.
    [string]$patchUri = ""

    if (![string]::IsNullOrEmpty($properties.objectId))
    {
      $patchUri = $patchUri,$properties.objectId -join "/"
    } Else {
      Write-Error "objectId is required"
    }

    $this.postBody = $properties.postBody
<#
    write-host "PATCH" $patchUri -ForegroundColor Magenta
    write-host "Body" ($this.postBody | ConvertTo-Json) -ForegroundColor Magenta
    Invoke-MgGraphRequest -Method PATCH -Uri $patchUri -Body ($this.postBody | ConvertTo-Json)
#>
    $this.invoke(@{
      Method = "PATCH"
      Uri = $patchUri
      Body = ($properties.postBody)
    })

  }
  #endregion patch

  #region delete
  [void] Delete([hashtable]$properties)
  {
    # Had to put the uri in a separate variable to avoid the uri in the instance of the class to be modified.
    [string]$deleteUri = ""

    if (![string]::IsNullOrEmpty($properties.objectId))
    {
      $deleteUri = $deleteUri,$properties.objectId -join "/"
    } Else {
      Write-Error "objectId is required"
    }

    $this.invoke(@{
      Method = "DELETE"
      Uri = $deleteUri
    })
  }
  #endregion delete

<# Not used at the moment. Will refactor when needed.
  #region validate required scopes
  [bool]validateRequiredScopes([hashtable]$properties)
  {
    $context = Get-MgContext

    If ($null -eq $context)
    {
      Write-Error "No active session found. Please connect to the Microsoft Graph API"
      exit
    } else {
      $sessionScopes = $context.Scopes
    }

    $requiredScopes = $properties.requiredScopes
    $missingScopes = $true

    foreach ($scope in $requiredScopes)
    {
      If ($sessionScopes -contains $scope)
      {
        $missingScopes = $false
      }
    }

    return $missingScopes

  }
  #end region validate required scopes
#>

  #region getowner
  [array]getOwner([string]$objectId)
  {
    # Check if objecttype can have owners
    if ($this.objectTypeList.item($this.currentObjectType).hasOwner -eq $true)
    {
      [string]$ownerURI = $objectId,"owners" -join "/"
      write-host "GET" $ownerURI -ForegroundColor Magenta
      $owners = $this.invoke(@{
        Method = "GET"
        Uri = $ownerURI
      })

      return $owners
    } else {
      Throw "Object type does not have owners"
    }
  }
  #endregion getowner

  #region deleteOwner
  [bool]deleteOwner([hashtable]$properties)
  {
    # Check if objecttype can have owners
    if ($this.objectTypeList.item($this.currentObjectType).hasOwner -eq $true)
    {
      $deleteOwnerURI = $($properties.objectId),"owners",$($properties.ownerId),"`$ref" -join "/"
      $this.invoke(@{
        Method = "DELETE"
        Uri = $deleteOwnerURI
      })

      return $true
    } else {
      Throw "Object type does not have owners"
    }
  }
  #endregion deleteOwner


  #region addOwner
  [bool]addOwner([hashtable]$properties)
  {
    # Check if objecttype can have owners
    if ($this.objectTypeList.item($this.currentObjectType).hasOwner -eq $true)
    {

      $body = @{
        "@odata.id" = "https://graph.microsoft.com/v1.0/directoryObjects/$($properties.ownerId)"
      }

      $addOwneruri = $($properties.objectId),"owners","`$ref" -join "/"

      $this.invoke(@{
        Method = "POST"
        Uri = $addOwneruri
        Body = $body
      })

      return $true
    } else {
      Throw "Object type does not have owners"
    }
  }
  #endregion addOwner

} #end of class definition

<# Example code on how to use the class
  $servicePrincipals = [GraphAL]::new(@{objectType = 'servicePrincipal'})
  $servicePrincipals.Get(@{objectId = '1d3b3e1f-2b4e-4b1e-8f4d-0f4f1f4f1f4f'}) # Returns one object
  $servicePrincipals.Get() # Returns all objects
#>
# https://learn.microsoft.com/en-us/graph/api/resources/conditionalaccesspolicy?view=graph-rest-1.0

class entraCAManagement : GraphAL
{

  entraCAManagement([hashtable]$properties) : base(@{
    apiVersion = $properties.apiVersion; 
    objectType = 'conditionalAccess'
  })

  {
    $this.uri =  $this.uri, $properties.componentName -join "/"
  }

}


<#
  How to use this class:

  componentName is a sub for the conditionalAccess API.

  ComponentName can be one of the following:
  * policies
  * namedLocations
  * authenticationContextClassReferences
  * templates

  Usage examples:
  
  * $caPolicyManagement = [entraRoleManagement]::new(@{apiVersion=$apiversion;componentName="policies"})
  
#>
<# 
  Directory roles can be managed directly through Entra ID or through PIM. This class 
  uses PIM rather than the Entra directory itself.
  
  The Graph API has endpoints for PIM integration. The endpoints in the Graph API map to 
  the PIM api.
  
  Since I dont want to overcomplicate the graphAL class, this class builds on the graphAL 
  class.

  https://learn.microsoft.com/en-us/graph/api/resources/rolemanagement?view=graph-rest-1.0
#>

class entraRoleManagement : GraphAL
{

  entraRoleManagement([hashtable]$properties) : base(@{
    apiVersion = $properties.apiVersion; 
    objectType = 'roleManagement'
  })

  {
    $this.uri =  $this.uri, $properties.serviceName, $properties.componentName -join "/"
  }

}


<#
  How to use this class:

  serviceName and componentName are both subs for the roleManagement API.

  serviceName can be one of the following:
  * directory
  * entitlementManagement

  ComponentName can be one of the following:
  * roleDefinitions
  * roleAssignments
  * roleEligibilitySchedules

  Usage examples:
  
  * $roleManagement = [entraRoleManagement]::new(@{apiVersion=$apiversion;serviceName="directory";componentName="roleDefinitions"})
  * $roleAssignments = [entraRoleManagement]::new(@{apiVersion=$apiversion;serviceName="directory";componentName="roleAssignments"})
  * $roleEligibilitySchedules = [entraRoleManagement]::new(@{apiVersion=$apiversion;serviceName="directory";componentName="roleEligibilitySchedules"})
  
#>
class graphObjectTypePermissions
{
  [hashtable]$typeList = @{
    servicePrincipal = @{
      apiRef = "servicePrincipals"
      apiPermissions = @{
        "read" = ("Application.Read.All","Application.ReadWrite.All", "Directory.Read.All", "Directory.ReadWrite.All")
        "modify" = ("Application.ReadWrite.All", "Directory.Read.All", "Directory.ReadWrite.All")
      }
      hasOwner = $true
    }

    application = @{
      apiRef = "applications"
      apiPermissions = @{
        "read" = ("Application.Read.All","Application.ReadWrite.All", "Directory.Read.All", "Directory.ReadWrite.All")
        "modify" = ("Application.ReadWrite.All", "Directory.Read.All", "Directory.ReadWrite.All")
      }
      hasOwner = $true
    }

    user = @{
      apiRef = "users"
      apiPermissions = @{
        "read" = ("User.Read.All", "User.ReadWrite.All", "Directory.Read.All", "Directory.ReadWrite.All")
        "modify" = ("User.ReadWrite.All", "Directory.Read.All", "Directory.ReadWrite.All")
      }
    }

    group = @{
      apiRef = "groups"
      apiPermissions = @{
        "read" = ("Group.Read.All", "Directory.Read.All")
        "modify" = ("Group.Read.All","Group.ReadWrite.All", "Directory.Read.All", "Directory.ReadWrite.All")
      }
      hasOwner = $true
    }

    roleManagement = @{
      apiRef = "roleManagement"
      apiPermissions = @{
        "read" = ("RoleManagement.Read.Directory", "Directory.Read.All")
        "modify" = ("RoleManagement.Read.Directory", "Directory.Read.All", "Directory.ReadWrite.All", "RoleManagement.ReadWrite.Directory")
      }
    }

    directoryObject = @{
      apiRef = "directoryObjects"
      apiPermissions = @{
        "read" = ("Directory.Read.All", "Directory.ReadWrite.All")
      }
    }

    conditionalAccessPolicies = @{
      apiRef = "identity/conditionalAccess/policies"
      apiPermissions = @{
        "read" = ("Policy.Read.All")
        "modify" = ("Policy.Read.All","Policy.ReadWrite.ConditionalAccess")
      }
    }

    conditionalAccess = @{
      apiRef = "identity/conditionalAccess"
      apiPermissions = @{
        "read" = ("Policy.Read.All")
        "modify" = ("Policy.Read.All","Policy.ReadWrite.ConditionalAccess")
      }
    }

    domain = @{
      apiRef = "domains"
      apiPermissions = @{
        "read" = ("Domain.Read.All","Directory.Read.All")
        "modify" = ("domain.ReadWrite.All","Directory.ReadWrite.All")
      }
    }
    
  }
  graphObjectTypePermissions() {$this.getAll()}
  [hashtable] getAll()
  {
    return $this.typeList
  }

}
<#

## This class doesnt work in Powershell 5, it needs powershell 7. 
## System.Management.Automation.IValidateSetValuesGenerator is a .NET core interface
## Leaving the class for future use if we ever move to powershell 7

class graphObjectTypeValidator : System.Management.Automation.IValidateSetValuesGenerator
{
  [hashtable]$typeList = [graphObjectTypePermissions]::new().getAll()

  [String[]] GetValidValues() {
    $Global:graphTypes = $this.typeList.keys
    return ($Global:graphTypes)
  }
}
#>
class IntuneDeviceComplianceAssignment {
    [string]$id
    [string]$source
    [string]$sourceId
    [IntuneDeviceComplianceAssignmentTarget[]]$target
}
class IntuneDeviceComplianceAssignmentTarget {
    [string]$deviceAndAppManagementAssignmentFilterId
    [string]$deviceAndAppManagementAssignmentFilterType
    [string]$groupId
}
class IntuneDeviceComplianceProfile {
    [System.Array]$roleScopeTagIds
    [string]$description
    [string]$displayName
    [string]$version
    [bool]$passwordRequired
    [bool]$passwordBlockSimple
    [bool]$passwordRequiredToUnlockFromIdle
    [string]$passwordMinutesOfInactivityBeforeLock
    [string]$passwordExpirationDays
    [string]$passwordMinimumLength
    [string]$passwordMinimumCharacterSetCount
    [string]$passwordRequiredType
    [string]$passwordPreviousPasswordBlockCount
    [bool]$requireHealthyDeviceReport
    [string]$osMinimumVersion
    [string]$osMaximumVersion
    [string]$mobileOsMinimumVersion
    [string]$mobileOsMaximumVersion
    [bool]$earlyLaunchAntiMalwareDriverEnabled
    [bool]$bitLockerEnabled
    [bool]$secureBootEnabled
    [bool]$codeIntegrityEnabled
    [bool]$storageRequireEncryption
    [bool]$activeFirewallRequired
    [bool]$defenderEnabled
    [string]$defenderVersion
    [bool]$signatureOutOfDate
    [bool]$rtpEnabled
    [bool]$antivirusRequired
    [bool]$antiSpywareRequired
    [bool]$deviceThreatProtectionEnabled
    [string]$deviceThreatProtectionRequiredSecurityLevel
    [bool]$configurationManagerComplianceRequired
    [bool]$tpmRequired
    [string]$deviceCompliancePolicyScript
    [System.Array]$validOperatingSystemBuildRanges
    [IntuneDeviceComplianceAssignment[]]$assignments
    [IntuneDeviceComplianceScheduledActions[]]$scheduledActionsForRule
}
class IntuneDeviceComplianceScheduledActionConfiguration {
    [string]$id
    [string]$gracePeriodHours
    [string]$actionType
    [string]$notificationTemplateId
    [system.array]$notificationMessageCCList
}
class IntuneDeviceComplianceScheduledActions {
    [string]$id
    [string]$ruleName
    [IntuneDeviceComplianceScheduledActionConfiguration[]]$scheduledActionConfigurations
}
class IntuneRbacRole {
    [string]$displayName
    [string]$description
    [IntuneRbacRoleResourceActions[]]$rolePermissions
    [bool]$isBuiltIn
}
class IntuneRbacRoleResourceActions {
    [IntuneRbacRoleResourceActionTypes[]]$resourceActions
}
class IntuneRbacRoleResourceActionTypes{
    [system.array]$allowedResourceActions
    [system.array]$notAllowedResourceActions
}
class PawGroup {
    [string]$displayName
    [string]$description
    [string]$id #this is samaccountname
    [string]$rule
    [string[]]$memberRef
    [bool]$isAssignedToRole
    [bool]$protectedGroup
    [bool]$isAppGroup
    PawGroup(){
        $this.memberRef = @()
    }
    PawGroup([string]$id){
        $this.id = $id
        $this.memberRef = @()
    }
}
class PIMAL
{


  #https://api.azrbac.mspim.azure.com/api/v2/privilegedAccess/aadroles/resources/712b220f-8e95-472b-bf02-24a48fe210bb/roleDefinitions
  #?$select=id,displayName,type,templateId,resourceId,externalId,isbuiltIn,subjectCount,eligibleAssignmentCount,activeAssignmentCount&$orderby=displayName
  [string] $tenantid
  [string] $api
  [string] $objectType
  [string] $action


  PIMAL()
  {
    # Get access tokem
    $this.tenantid = (Get-MgContext).TenantId
    $this.objectType = "aadroles"
    $this.action = "roleDefinitions"
    $this.api = "https://api.azrbac.mspim.azure.com/api/v2/privilegedAccess/$($this.objectType)/resources/$($this.tenantid)/$($this.action)"
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
class MdiRemoteReadiness {
    [string]$server
    [bool]$isDc
    [bool]$isAdwsReady
    [bool]$isWinRmReady
    [bool]$isRemoteRegReady
    [bool]$isMdiRemoteReady
    hidden [AdAl]$myDomain
    MdiRemoteReadiness([string]$server) {
        $this.server = $server
    }
    MdiRemoteReadiness([string]$server,[AdAl]$myDomain) {
        $this.server = $server
        $this.myDomain = $myDomain
    }
    [void]TestAllReadiness(){
        if ($this.myDomain -eq $null) {
            $this.myDomain = Initialize-MyDomain -domain $null -myDomain $this.myDomain 
        }
        if ($this.server -notmatch '\.') {
            $serverFqdn = $this.server+'.'+$($this.mydomain.domainfqdn)
        } else {
            $serverFqdn = $this.server
        }
        #isdc
        if ($serverFqdn -in $this.myDomain.domainDetail.ReplicaDirectoryServers) {
            $this.isDc = $true
        } else {
            $this.isDc = $false
            $this.isAdwsReady = $false
        }
        if ($this.isDc) {
            #isAdwsReady
            try {
                $socket = New-Object System.Net.Sockets.TcpClient( $serverFqdn, 9389 )
                if ($socket.Connected) {
                    $this.isAdwsReady = $true
                } else {
                    $this.isAdwsReady = $false
                }
            } catch {
                $this.isAdwsReady = $false
            }
        }
        #isWinRmReady
        try {
            $cmdCheck = $null
            $c = '$env:temp'
            $command  = "Invoke-command -scriptblock {$c} -ComputerName $serverFqdn"+' 2> $null'
            $cmdCheck += iex $command
            if ($cmdCheck) {
                $this.isWinRmReady = $true
            } else {
                $this.isWinRmReady = $false
            }
        } catch {
            $this.isWinRmReady = $false
        }
        #isRemoteRegReady
        $reg = $null
        $key = $null
        try {
            [Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey('LocalMachine', "$serverFqdn") | out-null
            $reg = [Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey('LocalMachine', "$serverFqdn")
            $key = $reg.OpenSubKey("SOFTWARE")
            if ($reg -and $key) {
                $this.isRemoteRegReady = $true
            } else {
                $this.isRemoteRegReady = $false
            }
        } catch {
            $this.isRemoteRegReady = $false
        } finally {
            if ($reg) {
                $reg.Close()
            }
            if ($key) {
                $key.Close()
            }
        }
        if ($this.isDc) {
            $this.isMdiRemoteReady = ($this.isAdwsReady -and $this.isWinRmReady)
        } else {
            $this.isMdiRemoteReady = ($this.isWinRmReady)
        }
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
class ClawDomainReadiness {
    $MachineAccountPassword = [PSCustomObject]@{
        GpoName = "MIRCAT - Reset Machine Account Password"
        GpoExists = $false
        GpoGuid = $null
    }
    $Tier0BaselineAudit = [PSCustomObject]@{
        GpoName = "Tier 0 - Baseline Audit Policies - Tier 0 Servers"
        GpoExists = $false
        GpoGuid = $null
    }
    $Tier0DisallowDSRMLogin = [PSCustomObject]@{
        GpoName = "Tier 0 - Disallow DSRM Login - DC ONLY"
        GpoExists = $false
        GpoGuid = $null
    }
    $Tier0DomainBlock = [PSCustomObject]@{
        GpoName = "Tier 0 - Domain Block - Top Level"
        GpoExists = $false
        GpoGuid = $null
    }
    $Tier0DomainControllers = [PSCustomObject]@{
        GpoName = "Tier 0 - Domain Controllers - DC Only"
        GpoExists = $false
        GpoGuid = $null
    }
    $Tier0ESXAdminsRestrictedGroup = [PSCustomObject]@{
        GpoName = "Tier 0 - ESX Admins Restricted Group - DC Only"
        GpoExists = $false
        GpoGuid = $null
    }
    $Tier0UserRightsAssignments = [PSCustomObject]@{
        GpoName = "Tier 0 - User Rights Assignments - Tier 0 Servers"
        GpoExists = $false
        GpoGuid = $null
    }
    $Tier0RestrictedGroups = [PSCustomObject]@{
        GpoName = "Tier 0 - Restricted Groups - Tier 0 Servers"
        GpoExists = $false
        GpoGuid = $null
    }
    $Tier1LocalAdminSplice = [PSCustomObject]@{
        GpoName = "Tier 1 - Tier 1 Operators in Local Admin - Tier 1 Servers"
        GpoExists = $false
        GpoGuid = $null
    }
    $Tier1UserRightsAssignments = [PSCustomObject]@{
        GpoName = "Tier 1 - User Rights Assignments - Tier 1 Servers"
        GpoExists = $false
        GpoGuid = $null
    }
    $Tier1RestrictedGroups = [PSCustomObject]@{
        GpoName = "Tier 1 - Restricted Groups - Tier 1 Servers"
        GpoExists = $false
        GpoGuid = $null
    }
    $Tier2LocalAdminSplice = [PSCustomObject]@{
        GpoName = "Tier 2 - Tier 2 Operators in Local Admin - Tier 2 Devices"
        GpoExists = $false
        GpoGuid = $null
    }
    $Tier2UserRightsAssignments = [PSCustomObject]@{
        GpoName = "Tier 2 - User Rights Assignments - Tier 2 Devices"
        GpoExists = $false
        GpoGuid = $null
    }
    $Tier2RestrictedGroups = [PSCustomObject]@{
        GpoName = "Tier 2 - Restricted Groups - Tier 2 Devices"
        GpoExists = $false
        GpoGuid = $null
    }
    $TierAllDisableSMB1 = [PSCustomObject]@{
        GpoName = "Tier ALL - Disable SMBv1 - Top Level"
        GpoExists = $false
        GpoGuid = $null
    }
    $TierAllDisableWdigest = [PSCustomObject]@{
        GpoName = "Tier ALL - Disable WDigest - Top Level"
        GpoExists = $false
        GpoGuid = $null
    }
    $gpoReport = @{}
    hidden [AdAl]$myDomain
    ClawDomainReadiness ([AdAl]$myDomain){
        $this.myDomain = $myDomain
        $this.InitObjects()
    }
    InitObjects(){
        $gpo = ($this.myDomain.GetGpo("$($this.MachineAccountPassword.GpoName)",$null))
        if ($gpo) {
            $this.MachineAccountPassword.GpoGuid = $gpo.Properties["cn"].trimstart('{').trimend('}')
            $this.MachineAccountPassword.GpoExists = $true
        }
        $gpo = $null
        $this.gpoReport.Add($this.MachineAccountPassword.GpoName,$this.MachineAccountPassword)

        $gpo = ($this.myDomain.GetGpo("$($this.Tier0BaselineAudit.GpoName)",$null))
        if ($gpo) {
            $this.Tier0BaselineAudit.GpoGuid = $gpo.Properties["cn"].trimstart('{').trimend('}')
            $this.Tier0BaselineAudit.GpoExists = $true
        }
        $gpo = $null
        $this.gpoReport.Add($this.Tier0BaselineAudit.GpoName,$this.Tier0BaselineAudit)

        $gpo = ($this.myDomain.GetGpo("$($this.Tier0DisallowDSRMLogin.GpoName)",$null))
        if ($gpo) {
            $this.Tier0DisallowDSRMLogin.GpoGuid = $gpo.Properties["cn"].trimstart('{').trimend('}')
            $this.Tier0DisallowDSRMLogin.GpoExists = $true
        }
        $gpo = $null
        $this.gpoReport.Add($this.Tier0DisallowDSRMLogin.GpoName,$this.Tier0DisallowDSRMLogin)

        $gpo = ($this.myDomain.GetGpo("$($this.Tier0DomainBlock.GpoName)",$null))
        if ($gpo) {
            $this.Tier0DomainBlock.GpoGuid = $gpo.Properties["cn"].trimstart('{').trimend('}')
            $this.Tier0DomainBlock.GpoExists = $true
        }
        $gpo = $null
        $this.gpoReport.Add($this.Tier0DomainBlock.GpoName,$this.Tier0DomainBlock)

        $gpo = ($this.myDomain.GetGpo("$($this.Tier0DomainControllers.GpoName)",$null))
        if ($gpo) {
            $this.Tier0DomainControllers.GpoGuid = $gpo.Properties["cn"].trimstart('{').trimend('}')
            $this.Tier0DomainControllers.GpoExists = $true
        }
        $gpo = $null
        $this.gpoReport.Add($this.Tier0DomainControllers.GpoName,$this.Tier0DomainControllers)

        $gpo = ($this.myDomain.GetGpo("$($this.Tier0ESXAdminsRestrictedGroup.GpoName)",$null))
        if ($gpo) {
            $this.Tier0ESXAdminsRestrictedGroup.GpoGuid = $gpo.Properties["cn"].trimstart('{').trimend('}')
            $this.Tier0ESXAdminsRestrictedGroup.GpoExists = $true
        }
        $gpo = $null
        $this.gpoReport.Add($this.Tier0ESXAdminsRestrictedGroup.GpoName,$this.Tier0ESXAdminsRestrictedGroup)
        
        $gpo = ($this.myDomain.GetGpo("$($this.Tier0UserRightsAssignments.GpoName)",$null))
        if ($gpo) {
            $this.Tier0UserRightsAssignments.GpoGuid = $gpo.Properties["cn"].trimstart('{').trimend('}')
            $this.Tier0UserRightsAssignments.GpoExists = $true
        }
        $gpo = $null
        $this.gpoReport.Add($this.Tier0UserRightsAssignments.GpoName,$this.Tier0UserRightsAssignments)

        $gpo = ($this.myDomain.GetGpo("$($this.Tier0RestrictedGroups.GpoName)",$null))
        if ($gpo) {
            $this.Tier0RestrictedGroups.GpoGuid = $gpo.Properties["cn"].trimstart('{').trimend('}')
            $this.Tier0RestrictedGroups.GpoExists = $true
        }
        $gpo = $null
        $this.gpoReport.Add($this.Tier0RestrictedGroups.GpoName,$this.Tier0RestrictedGroups)

        $gpo = ($this.myDomain.GetGpo("$($this.Tier1LocalAdminSplice.GpoName)",$null))
        if ($gpo) {
            $this.Tier1LocalAdminSplice.GpoGuid = $gpo.Properties["cn"].trimstart('{').trimend('}')
            $this.Tier1LocalAdminSplice.GpoExists = $true
        }
        $gpo = $null
        $this.gpoReport.Add($this.Tier1LocalAdminSplice.GpoName,$this.Tier1LocalAdminSplice)
        
        $gpo = ($this.myDomain.GetGpo("$($this.Tier1UserRightsAssignments.GpoName)",$null))
        if ($gpo) {
            $this.Tier1UserRightsAssignments.GpoGuid = $gpo.Properties["cn"].trimstart('{').trimend('}')
            $this.Tier1UserRightsAssignments.GpoExists = $true
        }
        $gpo = $null
        $this.gpoReport.Add($this.Tier1UserRightsAssignments.GpoName,$this.Tier1UserRightsAssignments)
        
        $gpo = ($this.myDomain.GetGpo("$($this.Tier1RestrictedGroups.GpoName)",$null))
        if ($gpo) {
            $this.Tier1RestrictedGroups.GpoGuid = $gpo.Properties["cn"].trimstart('{').trimend('}')
            $this.Tier1RestrictedGroups.GpoExists = $true
        }
        $gpo = $null
        $this.gpoReport.Add($this.Tier1RestrictedGroups.GpoName,$this.Tier1RestrictedGroups)

        $gpo = ($this.myDomain.GetGpo("$($this.Tier2LocalAdminSplice.GpoName)",$null))
        if ($gpo) {
            $this.Tier2LocalAdminSplice.GpoGuid = $gpo.Properties["cn"].trimstart('{').trimend('}')
            $this.Tier2LocalAdminSplice.GpoExists = $true
        }
        $gpo = $null
        $this.gpoReport.Add($this.Tier2LocalAdminSplice.GpoName,$this.Tier2LocalAdminSplice)

        $gpo = ($this.myDomain.GetGpo("$($this.Tier2UserRightsAssignments.GpoName)",$null))
        if ($gpo) {
            $this.Tier2UserRightsAssignments.GpoGuid = $gpo.Properties["cn"].trimstart('{').trimend('}')
            $this.Tier2UserRightsAssignments.GpoExists = $true
        }
        $gpo = $null
        $this.gpoReport.Add($this.Tier2UserRightsAssignments.GpoName,$this.Tier2UserRightsAssignments)

        $gpo = ($this.myDomain.GetGpo("$($this.Tier2RestrictedGroups.GpoName)",$null))
        if ($gpo) {
            $this.Tier2RestrictedGroups.GpoGuid = $gpo.Properties["cn"].trimstart('{').trimend('}')
            $this.Tier2RestrictedGroups.GpoExists = $true
        }
        $gpo = $null
        $this.gpoReport.Add($this.Tier2RestrictedGroups.GpoName,$this.Tier2RestrictedGroups)

        $gpo = ($this.myDomain.GetGpo("$($this.TierAllDisableSMB1.GpoName)",$null))
        if ($gpo) {
            $this.TierAllDisableSMB1.GpoGuid = $gpo.Properties["cn"].trimstart('{').trimend('}')
            $this.TierAllDisableSMB1.GpoExists = $true
        }
        $gpo = $null
        $this.gpoReport.Add($this.TierAllDisableSMB1.GpoName,$this.TierAllDisableSMB1)

        $gpo = ($this.myDomain.GetGpo("$($this.TierAllDisableWdigest.GpoName)",$null))
        if ($gpo) {
            $this.TierAllDisableWdigest.GpoGuid = $gpo.Properties["cn"].trimstart('{').trimend('}')
            $this.TierAllDisableWdigest.GpoExists = $true
        }
        $gpo = $null
        $this.gpoReport.Add($this.TierAllDisableWdigest.GpoName,$this.TierAllDisableWdigest)
    }
}
Class ClawGpo {
    [string]$name
    [string]$guid
    [System.Collections.Hashtable]$filePath
    [System.Collections.Hashtable]$content
    hidden [bool]$created
    ClawGpo ([string]$name) {
        $this.filePath = @{}
        $this.content = @{}
        $this.name = $name
    }
    ClawGpo ([string]$name,[string]$guid) {
        $this.filePath = @{}
        $this.content = @{}
        $this.guid = $guid
        $this.name = $name
    }
    ClawGpo ([string]$name,[string]$guid,[System.Collections.Hashtable]$filePath) {
        $this.filePath = @{}
        $this.content = @{}
        $this.guid = $guid
        $this.name = $name
        $this.filePath = $filePath
    }
    ClawGpo ([string]$name,[string]$guid,[System.Collections.Hashtable]$filePath,[System.Collections.Hashtable]$content) {
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
Class ClawGroup {
    [string]$name
    [string]$description
    [string]$dn
    ClawGroup ([string]$name,[string]$description,[string]$dn){
        $this.name = $name
        $this.description = $description
        $this.dn = $dn
    }
    ClawGroup ([string]$name,[string]$description){
        $this.name = $name
        $this.description = $description
    }
    [string]ToString(){
        return ("{0},{1},{2}" -f $this.name, $this.description, $this.dn)
    }
}
function Add-AdPrivRoleUserDataToFile {
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$true)]
        $adObject,
        [Parameter(Mandatory=$true)]
        [string]$parentGroup,
        [Parameter(Mandatory=$true)]
        $myDomain,
        [Parameter(Mandatory=$true)]
        [string]$outputFile
    )
    $uacDecoded = $myDomain.DecodeUserAccountControl([convert]::toint32(($adObject.Properties["useraccountcontrol"]),10))
    $isDelegated = $false
    $pwdNeverExpires = $false
    if ("NOT_DELEGATED" -notin $uacDecoded) {
        $isDelegated = $true
    }
    if ("DONT_EXPIRE_PASSWORD" -in $uacDecoded){
        $pwdNeverExpires = $true
    }
    try {
        $supportedEncryption = [convert]::toint32(($adObject.Properties["msds-supportedencryptiontypes"]),10)
        if (($supportedEncryption -eq 8) -or ($supportedEncryption -eq 16) -or ($supportedEncryption -eq 24)) {
            $strongAes = $true
        } else {
            $strongAes = $false
        }
    } catch {
        $strongAes = $false
    }
    "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12}\{13},{14},{15}" -f $(try { $($adObject.properties.samaccountname.tolower().tostring()) } catch {""}), $(try { $($($adObject.properties.objectclass) -join ":") } catch {""}), $(try { $(($adObject.properties.distinguishedname.tolower().tostring()).replace(',','_')) } catch {""}), $(try { $([datetime]::fromfiletime($($adObject.properties.pwdlastset)).tostring("MM/dd/yyyy hh:mm:ss")) } catch {""}), $(try { $([datetime]::fromfiletime($($adObject.properties.lastlogontimestamp)).tostring("MM/dd/yyyy hh:mm:ss")) } catch {""}), $pwdNeverExpires, $(try { $($adObject.properties.scriptpath) } catch {""}), $isDelegated, $(try { $([bool]$adObject.properties.serviceprincipalname) } catch {""}),$(try { $($myDomain.GetStringSidFromBytes([byte[]]$($adObject.properties.objectsid))) } catch {""}), $(try { $($adObject.properties.whencreated.ToShortDateString()) } catch {""}), $(try { $($adObject.properties.whenchanged.ToShortDateString()) } catch {""}), $myDomain.domainNetbiosName, $parentGroup,$(try { $(Get-AdRiskyUacReport -uacDecoded $uacDecoded -separator '|') } catch {""}),$strongAes  | add-content -path $outputFile  
}

function Add-EntraObjectOwner
{
  <#
    .SYNOPSIS
    Adds an owner to an object in the Entra system.

    .DESCRIPTION
    The Add-EntraObjectOwner function adds an owner to a specified object in the Entra system. The function requires the object type, object ID, and owner ID as mandatory parameters.

    .PARAMETER objectType
    Specifies the type of the object to which the owner will be added. The value should be a valid object type.

    .PARAMETER objectId
    Specifies the ID of the object to which the owner will be added. The value should be a valid object ID.

    .PARAMETER ownerID
    Specifies the ID of the owner to be added to the object. The value should be a valid owner ID.

    .EXAMPLE
    Add-EntraObjectOwner -objectType "User" -objectId "12345" -ownerID "67890"
    Adds the owner with ID "67890" to the object with ID "12345" of type "User" in the Entra system.

    .NOTES
    This function requires the GraphAL module to be imported.
  #>

  [CmdletBinding()]
  param
  (
    [Parameter(Mandatory = $true)]
    #[ValidateSet([graphObjectTypeValidator])] # This is not working in powershell 5.0
    [ValidateScript({
      $validValues = [graphObjectTypePermissions]::new().getAll().Keys
      if ($_ -in $validValues) {
          $true
      } else {
          throw "Invalid value '$_'. Valid values are: $($validValues -join ', ')"
      }
    })]
    [string] $objectType,

    [Parameter(Mandatory = $true)]
    [string] $objectId, # Guid format

    [Parameter(Mandatory = $true)]
    [string] $ownerID # Guid format
  )

  $graphCollection = [GraphAL]::new(@{objectType = $objectType})

  $params = @{
    objectId = $objectId
    ownerID = $ownerID
  }

  Try
  {
    $graphCollection.addOwner($params)
  }
  Catch
  {
    Write-Error $_.Exception.Message
  }
}
function Add-GpoApplyAcl
{
    [CmdletBinding()]
    Param(
    [parameter(Mandatory=$True,Position=1)]
    [string]$gpoGuid,
    [parameter(Mandatory=$True)]
    [string]$identity,
    [parameter(Mandatory=$True)]
    [ValidateSet("Allow","Deny")]
    [string]$permissionType,
    [Parameter(Mandatory=$true)]
    [ValidateSet("Forest","Domain")]
    [string]$identityLocation,
    [Parameter(DontShow)]
    [string]$domain,
    [Parameter(DontShow)]
    $myDomain
    )
    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
    if ($identityLocation.ToUpper() -eq "FOREST") {
        $netBios = $myDomain.forestNetbiosName
    } else {
        $netBios = $myDomain.domainNetbiosName
    }
    $returnVal = $false
    $objtoAcl = "$netBios\$identity"
    $adsiGpo = [ADSI]"LDAP://CN=`{$($gpoGuid)`},CN=Policies,CN=System,$($myDomain.domainDn)"
    if ($adsiGpo) {
        $rule = New-Object System.DirectoryServices.ActiveDirectoryAccessRule(
        [System.Security.Principal.NTAccount]"$objtoAcl",
        "ExtendedRight",
        $permissionType,
        [Guid]"edacfd8f-ffb3-11d1-b41d-00a0c968f939"
        )
        $acl = $adsiGpo.ObjectSecurity
        $acl.AddAccessRule($rule) | out-null
        try {
            $adsiGpo.CommitChanges() | out-null
            $returnVal = $true
        }
        catch {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Add ACL failed to commit changes. $($_.Exception)" -logSev "Error" | out-null
            $returnVal = $false
        }
        
    } else {
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Add ACL reports GPO not found. $($_.Exception)" -logSev "Error" | out-null
        $returnVal=$false
    }
    return $returnVal
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
function Add-MDIGmsaRetrievalGroup {
    
    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$true)]
        [string]$identity,
        [Parameter(Mandatory=$true)]
        [string]$group
    )

    $existingGroup = (Get-ADServiceAccount -Identity $identity -Properties *).PrincipalsAllowedToRetrieveManagedPassword
    $existingGroup += (Get-ADGroup $group -Properties *).distinguishedname
    Set-ADServiceAccount -Identity $identity -PrincipalsAllowedToRetrieveManagedPassword $existingGroup
    Get-ADServiceAccount -Identity $identity -Properties * | fl PrincipalsAllowedToRetrieveManagedPassword 
}
function Connect-Az {
    [CmdletBinding()]
    Param (
        [Parameter(Mandatory=$true)]
        [string]$tenantId
    )
    try {
        if (!([bool](Get-InstalledModule "AzureADPreview" -erroraction silentlycontinue))) {
            throw
        }
    } catch {
        Install-module AzureADPreview -confirm:$false -force
    }
    try {
        if (!([bool](Get-InstalledModule "az.resources" -erroraction silentlycontinue))) {
            throw
        }
    } catch {
        Install-module az.resources -confirm:$false -force
    }
    try {
        if (!([bool](Get-InstalledModule "az.accounts" -erroraction silentlycontinue))) {
            throw
        }
    } catch {
        Install-module az.accounts -confirm:$false -force
    }
    import-module AzureADPreview -force
    import-module az.resources -force
    import-module az.accounts -force
    try {
        Get-AzureADTenantDetail -ErrorAction STOP
    } catch {
        Connect-azuread -TenantId $tenantId
    }
}
function Connect-CloudAll
{
    [CmdletBinding()]
    Param (
        [Parameter(Mandatory=$true)]
        [string]$tenantId
    )
    try {
        if (!([bool](Get-InstalledModule "Microsoft.Graph.Identity.SignIns" -erroraction silentlycontinue))) {
            throw
        }
    } catch {
        Install-module Microsoft.Graph.Identity.SignIns -confirm:$false -force
    }
    try {
        if (!([bool](Get-InstalledModule "Microsoft.Graph.Authentication" -erroraction silentlycontinue))) {
            throw
        }
    } catch {
        Install-Module Microsoft.Graph.Authentication -confirm:$false -force
    }
    try {
        if (!([bool](Get-InstalledModule "AzureADPreview" -erroraction silentlycontinue))) {
            throw
        }
    } catch {
        Install-module AzureADPreview -confirm:$false -force
    }
    try {
        if (!([bool](Get-InstalledModule "az.resources" -erroraction silentlycontinue))) {
            throw
        }
    } catch {
        Install-module az.resources -confirm:$false -force
    }
    try {
        if (!([bool](Get-InstalledModule "az.accounts" -erroraction silentlycontinue))) {
            throw
        }
    } catch {
        Install-module az.accounts -confirm:$false -force
    }
    import-module Microsoft.Graph.Authentication -force
    import-module Microsoft.Graph.Identity.SignIns -force
    import-module AzureADPreview -force
    import-module az.resources -force
    import-module az.accounts -force
    try {
        Get-AzureADTenantDetail -ErrorAction STOP
    } catch {
        Connect-azuread -TenantId $tenantId
    }
    try {
        Get-AzAccessToken -ErrorAction STOP
    } catch {
        Connect-AzAccount -TenantId $tenantId
    }
    if (!(get-mgcontext)) {
        connect-MgGraph -TenantId $tenantId
    }
}
function Connect-Mg {
    [CmdletBinding()]
    Param (
        [Parameter(Mandatory=$true)]
        [string]$tenantId
    )
    try {
        if (!([bool](Get-InstalledModule "Microsoft.Graph.Identity.SignIns" -erroraction silentlycontinue))) {
            throw
        }
    } catch {
        Install-module Microsoft.Graph.Identity.SignIns -confirm:$false -force
    }
    try {
        if (!([bool](Get-InstalledModule "Microsoft.Graph.Identity.Governance" -erroraction silentlycontinue))) {
            throw
        }
    } catch {
        Install-module Microsoft.Graph.Identity.Governance -confirm:$false -force
    }
    try {
        if (!([bool](Get-InstalledModule "Microsoft.Graph.Authentication" -erroraction silentlycontinue))) {
            throw
        }
    } catch {
        Install-Module Microsoft.Graph.Authentication -confirm:$false -force
    }
    try {
        if (!([bool](Get-InstalledModule "Microsoft.Graph.Applications" -erroraction silentlycontinue))) {
            throw
        }
    } catch {
        Install-Module Microsoft.Graph.Applications -confirm:$false -force
    }
    try {
        if (!([bool](Get-InstalledModule "Microsoft.Graph.Identity.DirectoryManagement" -erroraction silentlycontinue))) {
            throw
        }
    } catch {
        Install-Module Microsoft.Graph.Identity.DirectoryManagement -confirm:$false -force
    }
    try {
        if (!([bool](Get-InstalledModule "Microsoft.Graph.DirectoryObjects" -erroraction silentlycontinue))) {
            throw
        }
    } catch {
        Install-Module Microsoft.Graph.DirectoryObjects -confirm:$false -force
    }
    try {
        if (!([bool](Get-InstalledModule "Microsoft.Graph.Reports" -erroraction silentlycontinue))) {
            throw
        }
    } catch {
        Install-Module Microsoft.Graph.Reports -confirm:$false -force
    }
    try {
        if (!([bool](Get-InstalledModule "Microsoft.Graph.Users" -erroraction silentlycontinue))) {
            throw
        }
    } catch {
        Install-Module Microsoft.Graph.Users -confirm:$false -force
    }
    import-module Microsoft.Graph.Authentication -force
    import-module Microsoft.Graph.Applications -force
    import-module Microsoft.Graph.Identity.SignIns -force
    import-module Microsoft.Graph.Identity.DirectoryManagement -force
    import-module Microsoft.Graph.DirectoryObjects -force
    import-module Microsoft.Graph.Identity.Governance -force
    import-module Microsoft.Graph.Reports -force
    import-module Microsoft.Graph.Users -force
    if (!(get-mgcontext)) {
        connect-MgGraph -Scopes "AuditLog.Read.All","Directory.Read.All","Directory.ReadWrite.All","RoleManagement.Read.Directory","RoleManagement.ReadWrite.Directory","Policy.Read.All","Group.Read.All","Group.ReadWrite.All","GroupMember.Read.All","DeviceManagementApps.Read.All","DeviceManagementApps.ReadWrite.All","DeviceManagementConfiguration.Read.All","DeviceManagementConfiguration.ReadWrite.All","DeviceManagementManagedDevices.Read.All","DeviceManagementManagedDevices.ReadWrite.All","DeviceManagementServiceConfig.Read.All","DeviceManagementServiceConfig.ReadWrite.All","User.Read.All","User.ReadBasic.All","User.ReadWrite.All","Application.Read.All","Application.ReadWrite.All","RoleManagement.Read.Directory","RoleManagement.ReadWrite.Directory" -TenantId $tenantId
    }
}
function Convert-EntraSyncedToCloudOnly
{
  [CmdletBinding()]
  param (
    # Tenant ID
    [Parameter(Mandatory = $true)]
    [string]
    $TenantID,

    # Will export user objects to a JSON file
    [Parameter(ParameterSetName = 'ExportObjects', Position = 0)]
    [switch]
    $ExportObjects,

    # Output file for exporting user data. Must be a JSON file.
    [Parameter(ParameterSetName = 'ExportObjects', Position = 1)]
    [string]
    $outputfilepath,

    # Disables sync on the tenant.
    [Parameter()]
    [switch]
    $DisableSync
  )

  $LogDate = Get-Date -Format "yyyyMMdd-HHmmss"

  # empty array to store scopes required for the specified actions
  $scopes = @()

  # Required scopes for reading all user properties
  if ($ExportObjects)
  {
    $scopes += "User.Read.All"
  }

  # Required scopes for updating the sync status
  if ($DisableSync)
  {
    $scopes += "Organization.ReadWrite.All"
  }

  # no point in continuing if no actions are specified
  if ($scopes.Count -eq 0)
  {
    Write-Error -Message "No action specified. Please specify an action to perform."
    exit
  }

  <#
  # Connect to Microsoft Graph API
  Try
  {
    Connect-MgGraph -Scopes $scopes -TenantId $TenantID
  } catch {
    Write-Error -Message "Failed to connect to Microsoft Graph API. Error: $_"
    exit
  }
#>

  #region ExportObjects
  if ($ExportObjects)
  # Will export synced users to a JSON file.
  {

    If ($outputfilepath -eq "")
    {
      Write-Error -Message "No output file specified. Please specify an output file for the user data."
      exit
    }
    
    # Retrieve users using the Microsoft Graph API with property
    $propertyParams = @{
        #All            = $true
        objecttype = 'user'
        Property = 
            'id,
            givenName,
            surname,
            displayName,
            userPrincipalName,
            userType,
            onPremisesSyncEnabled,
            accountEnabled,
            OnPremisesDistinguishedName,
            OnPremisesDomainName,
            OnPremisesExtensionAttributes,
            OnPremisesImmutableId,
            OnPremisesLastSyncDateTime,
            OnPremisesProvisioningErrors,
            OnPremisesSamAccountName,
            OnPremisesSecurityIdentifier,
            OnPremisesSyncEnabled,
            OnPremisesUserPrincipalName'

        Filter = "OnPremisesSyncEnabled eq true"
    }

    #$users = Get-MgUser @propertyParams
    $users_Graph = [GraphAL]::new($propertyParams)
    $users = ($users_Graph.Get(@{})).Value
    $totalUsers = $users.Count
    write-host "totalusers :" $totalUsers

    # Initialize progress counter
    $progress = 0

    # Initialize an array to store user objects
    $userObjects = @()

    # Loop through all users and collect user objects
    foreach ($index in 0..($totalUsers - 1)) 
    {
      $user = $users[$index]

      # Update progress counter
      $progress++

      # Calculate percentage complete
      $percentComplete = ($progress / $totalUsers) * 100

      # Define progress bar parameters
      $progressParams = @{
          Activity        = "Processing Users"
          Status          = "User $($index + 1) of $totalUsers - $($user.userPrincipalName) - $($percentComplete -as [int])% Complete"
          PercentComplete = $percentComplete
      }

      # Display progress bar
      Write-Progress @progressParams

      $userObject = [PSCustomObject]@{
          "ID"                            = $user.id
          "First name"                    = $user.givenName
          "Last name"                     = $user.surname
          "Display name"                  = $user.displayName
          "User principal name"           = $user.userPrincipalName
          "User type"                     = $user.userType
          "On-Premises sync"              = if ($user.onPremisesSyncEnabled) { "enabled" } else { "disabled" }
          "Account status"                = if ($user.accountEnabled) { "enabled" } else { "disabled" }
          "OnPremisesDistinguishedName"   = $user.OnPremisesDistinguishedName
          "OnPremisesDomainName"          = $user.OnPremisesDomainName
          "OnPremisesExtensionAttributes" = $user.OnPremisesExtensionAttributes
          "OnPremisesImmutableId"         = $user.OnPremisesImmutableId
          "OnPremisesLastSyncDateTime"    = $user.OnPremisesLastSyncDateTime
          "OnPremisesProvisioningErrors"  = $user.OnPremisesProvisioningErrors
          "OnPremisesSamAccountName"      = $user.OnPremisesSamAccountName
          "OnPremisesSecurityIdentifier"  = $user.OnPremisesSecurityIdentifier
          "OnPremisesSyncEnabled"         = $user.OnPremisesSyncEnabled
          "OnPremisesUserPrincipalName"   = $user.OnPremisesUserPrincipalName
      }

        # Add user object to the array
        $userObjects += $userObject
    }

    # Complete the progress bar
    Write-Progress -Activity "Processing Users" -Completed

    if (Test-Path $outputfilepath)
    {
      $newfilepath = $outputfilepath.split(".")[0] + "." + $logdate + ".json"
      move-item $outputfilepath $newfilepath
    }
    # Export all user objects to CSV
    $userObjects | convertto-json | out-file $outputfilepath
  }
  #endregion ExportObjects

  #region DisableSync
  if ($DisableSync)
  # Disables sync on the tenant.
  {
    Write-Warning -Message "Disabling sync on the tenant. Be careful as it can take up to 72 hours to take effect."
    $safeword = "Yes, I would really like to continue with disabling sync on the tenant"
    $continue = Read-Host "Type `"$safeword`" or any other key to cancel"

    if ($continue -eq $safeword)
    {
      #$currentsyncstate = Get-MgOrganization | Select-Object OnPremisesSyncEnabled
      #$currentsyncstate = (Invoke-MgGraphRequest -Method Get -Uri "https://graph.microsoft.com/v1.0/organization").value.onPremisesSyncEnabled
      $graphAL = [GraphAL]::new(@{}) 
      $currentsyncstate = ($graphal.invoke(@{uri='organization';Method="GET"})).Value.onPremisesSyncEnabled

      If ($currentsyncstate -eq $false)
      {
        Write-Host "Sync is already disabled on the tenant. No action taken."
      } Else {
        #$organizationId = (Invoke-MgGraphRequest -Method Get -Uri "https://graph.microsoft.com/v1.0/organization").value.Id
        $organizationId = ($graphal.invoke(@{uri='organization';Method="GET"})).Value.Id

        $params = @{
          onPremisesSyncEnabled = $false
        }

        Write-host "Disabling sync on tenant on" (get-date) -ForegroundColor Yellow

        # Perform the update
        try {
          #Update-MgOrganization -OrganizationId $organizationId -BodyParameter $params
          #Invoke-MgGraphRequest -Method Patch -Uri "https://graph.microsoft.com/v1.0/organization/$organizationId" -Body $params
          $graphal.invoke(@{uri="organization/$organizationId";Method="PATCH";Body=$params})
        }
        catch {
          Write-Error -Message "Failed to disable sync on the tenant. Error: $_"
        }

        # Check that the command worked
        #$syncstate = (Invoke-MgGraphRequest -Method Get -Uri "https://graph.microsoft.com/v1.0/organization").value.onPremisesSyncEnabled
        $syncstate = ($graphal.invoke(@{uri='organization';Method="GET"})).Value.onPremisesSyncEnabled
        Write-Host $syncstate
      }
    } Else {
      Write-Host "You have chickened out." -ForegroundColor Yellow
    }
  }
  #endregion DisableSync
}
function disable-oldcapolicies 
{

  [CmdletBinding()]
  param (
    [Parameter(Mandatory = $true)]
    [string]
    $tenantid,

    [Parameter()]
    [string]
    $apiversion = "v1.0",

    [Parameter()]
    [string]
    $policyPrefix = "[IR]",

    [Parameter()]
    [string]
    $exportFile = "ConditionalAccessPoliciesBackup.json",

    [Parameter()]
    [switch]
    $restore
  )

  $scopes = "Policy.Read.All", 
            "Policy.ReadWrite.ConditionalAccess"

  try {
    Connect-MgGraph -tenantid $tenantid -Scopes $scopes  
  } catch {
    Write-Error -Message "Failed to connect to Microsoft Graph API. Error: $_"
    exit
  }

  $policyPrefix = [regex]::Escape($policyPrefix)

  $existingpolicies = (Invoke-MgGraphRequest -Method GET -Uri "https://graph.microsoft.com/$apiversion/identity/conditionalAccess/policies").value

  if(-not $restore)
  {
    if (Test-Path $exportFile) 
    {
      $date = Get-Date -Format "yyyyMMddHHmmss"
      Rename-Item $exportFile -NewName "$exportFile-$date.json"
    }
    $existingpolicies | ConvertTo-Json -Depth 10 | Out-File $exportFile

    Foreach ($policy in $existingpolicies | Where-Object {$_.templateId -eq $null `
          -and $_.state -eq "enabled" `
          -and $_.displayName -notmatch "$policyPrefix*"})
    {
      $postbody = @{
        state = "enabledForReportingButnotEnforced"
      }
      Write-Host "Disabling policy $($policy.displayName)" -ForegroundColor Magenta
      Invoke-MgGraphRequest -Method PATCH `
        -Uri "https://graph.microsoft.com/$apiversion/identity/conditionalAccess/policies/$($policy.id)" `
          -Body $postbody
    }
  }

  If($restore)
  {
    $policies = Get-Content $exportFile | ConvertFrom-Json
    Foreach ($policy in ($policies | Where-Object {$_.templateId -eq $null}))
    {
      Write-Host "Setting $($policy.state) for $($policy.displayName)" -ForegroundColor Magenta
      $postbody = @{
        state = $policy.state
      }
      Invoke-MgGraphRequest -Method PATCH `
        -Uri "https://graph.microsoft.com/$apiversion/identity/conditionalAccess/policies/$($policy.id)" `
        -Body $postbody
    }
  }
}
function Export-MircatLogBundle {
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$false)]
        [string]$ExportPath = ('{0}\{1}' -f $env:USERPROFILE,"Desktop")
    )

    $outputFileName = 'MIRCATBUNDLE-{0}.zip' -f (Get-Date).ToString("yy-MM-ddTHHmmss")

    $logLocationArray = @(
        "$env:temp\adalops.log",
        "$env:temp\claw.log",
        "$env:temp\mircatmdi.log"
        "$env:systemroot\temp\adalops.log"
        "$env:systemroot\temp\mircatmdi.log"
        "$env:systemroot\temp\mss\mdisetup\*.log"
    )
    $availableLogFileArray = @()
    foreach ($path in $logLocationArray) {
        if (Test-Path $path) {
            $availableLogFileArray += $path
        }
    }

    if (Test-Path ('{0}\{1}' -f $ExportPath, $outputFileName)) { Remove-Item ('{0}\{1}' -f $ExportPath, $outputFileName) -Force}
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $compressionLevel = [System.IO.Compression.CompressionLevel]::Fastest

    try {
        $zip = [System.IO.Compression.ZipFile]::Open(('{0}\{1}' -f $ExportPath, $outputFileName), [System.IO.Compression.ZipArchiveMode]::Create)
        foreach ($fullName in $availableLogFileArray) {
            $rname = $(Resolve-Path -Path $fullName) -replace '^\.\\',''
            Write-Output "Adding $rname"
            $null = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $rname, (Split-Path $rname -Leaf), $compressionLevel)
        }
        Write-Output "Zip file created at $('{0}\{1}' -f $ExportPath, $outputFileName)"
    } catch {
        Write-Warning "Failed to create bundle $($_.Exception)"
    } finally {
       $zip.Dispose()
    }
}
function Get-AdAdminCount
{
    ##########################################################################################################
    <#
    .SYNOPSIS
        Gets accounts where the adminCount=1 but these are not real admins.
    
    .DESCRIPTION
        Run Get-AdAdminCount and it will seek.

    .EXAMPLE
        Get-AdAdminCount

    .OUTPUTS
        Writes to console

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
    Param(
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )
    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
    $adminAccounts = $myDomain.GetObjectByFilter("&((adminCount=1)(objectClass=user)(!(samaccountname=krbtgt)))","domain")
    $privilegedGroups = $myDomain.privilegedgroupnames.values[0].split("`n")

    $returnVal = @()
    $adminAccounts | % { 
        $memberOfArray=@()
        if ([bool]$_.Properties.memberof) {
            $_.Properties.memberof | % { $memberOfArray += ($_.split(',')[0]).trim('CN=') }
            $check=$memberOfArray | ? -FilterScript {$_ -in $privilegedGroups}
        } else {
            $check = $false
        }
        if (!($check)) {
            $returnVal += $($_.properties.distinguishedname)
        }
    }
    return $returnVal
}
function Get-AdDomainInfo {
    ##########################################################################################################
    <#
    .SYNOPSIS
        Returns a simple dashboard about AD.
    
    .DESCRIPTION
        Run  Get-AdDomainInfo and it will seek.

    .EXAMPLE
         Get-AdDomainInfo

    .OUTPUTS
        Writes to console

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
    Param(
        [Parameter(mandatory=$false)]
        [string]$domain
    )
    try {
        $myDomain = [AdAl]::new()
        if ($domain) {
            $myDomain.AutoDomainDiscovery($domain)
        } else {
            $myDomain.AutoDomainDiscovery($null)
        }
        if (!($myDomain)) {
            throw
        }
    } catch {
        write-error "Failed to discover AD."
        throw
    }
    return $myDomain
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
function Get-AdKerbDelegatedAccounts
{
    <#
    .Synopsis
        Search the domain for accounts with Kerberos Delegation
    .DESCRIPTION
        Kerberos Delegation is a security sensitive configuration. Especially
        full (unconstrained) delegation has significant impact: any service
        that is configured with full delegation can take any account that
        authenticates to it, and impersonate that account for any other network 
        service that it likes. So, if a Domain Admin were to use that service,
        the service in turn could read the hash of KRBRTG and immediately 
        effectuate a golden ticket. Etc :)
        
        This scripts searches AD for regular forms of delegation: full, constrained,
        and resource based. It dumps the account names with relevant information (flags)
        and adds a comment field for special cases. The output is a PSObject that
        you can use for further analysis. 
        
        Note regarding resource based delegation: the script dumps the target 
        services, not the actual service doing the delegation. I did not bother 
        to parse that out. 
        
        Main takeaway: chase all services with unconstrained delegation. If 
        these are _not_ DC accounts, reconfigure them with constrained delegation, 
        OR claim them als DCs from a security perspective. Meaning, that the AD 
        team manages the service and the servers it runs on. 

    .EXAMPLE
    Get-AdKerbDelegatedAccounts | out-gridview
    .EXAMPLE
    Get-AdKerbDelegatedAccounts -DN "ou=myOU,dc=sol,dc=local"
    .NOTES
        Version:        0.1 : first version. 
                        0.2 : expanded LDAP filter and comment field.
        Author:         Willem Kasdorp, Microsoft. 
        Creation Date:  1/10/2016
        Last modified:  4/11/2017
    #>

    [CmdletBinding()]
    Param
    (
        [Parameter(mandatory=$false)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain,
        # start the search at this DN. Default is to search all of the domain.
        [string]$DN = (Get-ADDomain).DistinguishedName
    )
    $SERVER_TRUST_ACCOUNT = 0x2000
    $TRUSTED_FOR_DELEGATION = 0x80000
    $TRUSTED_TO_AUTH_FOR_DELEGATION= 0x1000000
    $PARTIAL_SECRETS_ACCOUNT = 0x4000000  

    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain

    $propertylist = @(
    "servicePrincipalname", 
    "useraccountcontrol", 
    "samaccountname", 
    "msDS-AllowedToDelegateTo", 
    "msDS-AllowedToActOnBehalfOfOtherIdentity"
    )
    $accounts = [system.collections.arraylist]::new()
    Get-ADServiceAccount -Filter  {(TrustedForDelegation -eq $True)} -Properties $propertylist -server $myDomain.chosenDc | % {$accounts.add($_) | out-null}
    Get-ADUser -Filter {(TrustedForDelegation -eq $True)} -Properties $propertylist -server $myDomain.chosenDc| % {$accounts.add($_) | out-null}
    Get-ADComputer -Filter {(TrustedForDelegation -eq $True)} -Properties $propertylist -server $myDomain.chosenDc | % {$accounts.add($_) | out-null}
    Foreach ($account in $accounts) {
        $isDC = ($account.useraccountcontrol -band $SERVER_TRUST_ACCOUNT) -ne 0
        $fullDelegation = ($account.useraccountcontrol -band $TRUSTED_FOR_DELEGATION) -ne 0
        $constrainedDelegation = ($account.'msDS-AllowedToDelegateTo').count -gt 0
        $isRODC = ($account.useraccountcontrol -band $PARTIAL_SECRETS_ACCOUNT) -ne 0
        $resourceDelegation = $account.'msDS-AllowedToActOnBehalfOfOtherIdentity' -ne $null
        
        $comment = ""
        if ((-not $isDC) -and $fullDelegation) { 
            $comment += "WARNING: full delegation to non-DC is not recommended!; " 
        }
        if ($isRODC) { 
            $comment += "WARNING: investigation needed if this is not a real RODC; " 
        }
        if ($resourceDelegation) { 
            # to count it using PS, we need the object type to select the correct function... broken, but there we are. 
            $comment += "INFO: Account allows delegation FROM other server(s); " 
        }
        if ($constrainedDelegation) { 
            $comment += "INFO: constrained delegation service count: $(($account.'msDS-AllowedToDelegateTo').count); " 
        }
        if ($isDC) { 
            $comment += "INFO: this is a Domain Controller and should not be modified!;"
        }

        [PSCustomobject] @{
            samaccountname = $account.samaccountname
            objectClass = $account.objectclass        
            uac = ('{0:x}' -f $account.useraccountcontrol)
            isDC = $isDC
            isRODC = $isRODC
            fullDelegation = $fullDelegation
            constrainedDelegation = $constrainedDelegation
            resourceDelegation = $resourceDelegation
            servicePrincipalname = $account.servicePrincipalname
            comment = $comment
        }
    }
}
function Get-AdPrivGroupNames {
    [CmdletBinding()]
    Param(
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )

    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
    $privilegedGroups = @()
    $privilegedGroups += "DNSAdmins"
    try {
        $privilegedGroups += (get-adgroup -filter "sid -eq '$($mydomain.domainSid)-512'" -Server $myDomain.chosenDc -erroraction silentlycontinue).samaccountname
    } catch {}
    try {
        $privilegedGroups += (get-adgroup -filter "sid -eq '$($mydomain.forestSid)-519'" -Server $myDomain.forestfqdn -erroraction silentlycontinue).samaccountname
    } catch {}
    try {
        $privilegedGroups += (get-adgroup -filter "sid -eq '$($mydomain.forestSid)-518'" -Server $myDomain.forestfqdn -erroraction silentlycontinue).samaccountname
    } catch {}
    try {
        $privilegedGroups += (get-adgroup -filter "sid -eq 'S-1-5-32-544'" -Server $myDomain.chosenDc -erroraction silentlycontinue).samaccountname
    } catch {}
    try {
        $privilegedGroups += (get-adgroup -filter "sid -eq 'S-1-5-32-548'" -Server $myDomain.chosenDc -erroraction silentlycontinue).samaccountname
    } catch {}
    try {
        $privilegedGroups += (get-adgroup -filter "sid -eq 'S-1-5-32-551'" -Server $myDomain.chosenDc -erroraction silentlycontinue).samaccountname
    } catch {}
    try {
        $privilegedGroups += (get-adgroup -filter "sid -eq 'S-1-5-32-549'" -Server $myDomain.chosenDc -erroraction silentlycontinue).samaccountname
    } catch {}
    try {
        $privilegedGroups += (get-adgroup -filter "sid -eq 'S-1-5-32-550'" -Server $myDomain.chosenDc -erroraction silentlycontinue).samaccountname
    } catch {}
    try {
        $privilegedGroups += (get-adgroup -filter "sid -eq '$($myDomain.domainSid)-520'" -Server $myDomain.chosenDc -erroraction silentlycontinue).samaccountname
    } catch {}
    return $privilegedGroups
}
function Get-AdPrivRoleReport
{
    ##########################################################################################################
    <#
    .SYNOPSIS
        Get's privileged group report from AD.
    
    .DESCRIPTION
        This function supports type modes of operation: Standard IR (by means of the -standardIR switch), and
        Single Object (by means of the -object switch). Standard IR mode will check 'Domain Admins',
        'Enterprise Admins','Schema Admins','Account Operators','DNSAdmins','Group Policy Creator Owners',
        'Backup Operators','Server Operators','Print Operators'. The function will recurse group membership for nesting checks. 

        The -object switch takes a DN or a samaccountname of a group. You can pass the -recurse
        switch to recursively check nested members.


    .EXAMPLE
        Get-AdPrivRoleReport -standardIR

        Run this in a quick mode for the typical checks against privileged groups in AD.
    .EXAMPLE
        Get-AdPrivRoleReport -object "domain admins"

        Run this to check the membership of the specific group Domain Admins, without doing recursion for nesting.
    .EXAMPLE
        Get-AdPrivRoleReport -object "domain admins" -recurse

        Run this to check the membership of the specific group Domain Admins, with doing recursion for nesting.

    .OUTPUTS
        Writes to file in $env:temp

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

    [CmdletBinding(DefaultParameterSetName = 'standardir')]
    Param(
        [Parameter(Mandatory=$false, ParameterSetName='standardir')]
        [switch]$standardIR=$true,
        [Parameter(Mandatory=$false, ParameterSetName='standardir')]
        [switch]$forestMode,
        [Parameter(Mandatory=$true, ParameterSetName='object')]
        [string]$object,
        [Parameter(Mandatory=$false, ParameterSetName='object')]
        [switch]$recurse,
        [Parameter(Mandatory=$false, ParameterSetName='object')]
        [Parameter(Mandatory=$false, ParameterSetName='standardir')]
        [switch]$OutputToConsole,
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain,
        [Parameter(DontShow)]
        [string]$outputFile
    )
    $searchedGroups = @()
    $domainList = [System.Collections.Generic.List[AdAl]]::new()
    if ($forestMode) {
        $domainList = Get-DomainsInForestAsAdalList
    } else {
        $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
        $domainList.Add($(Initialize-MyDomain -domain $domain -myDomain $myDomain))
    }
    
    if ($PSCmdlet.ParameterSetName -eq 'standardir') {
        foreach ($dDomain in $domainList) {
            if ([string]::IsNullOrEmpty($outputFile)) {
                if ($forestMode) {
                    $tag = $($dDomain.forestNetbiosName)
                } else {
                    $tag = $($dDomain.domainNetbiosName)
                }
                $outputFile = "{0}\{1}-{2}-{3}.{4}" -f $env:temp, "AdAudit-standardIR",$tag,$((get-date).tostring("yyyy-MM-ddTHHmmss")), "csv"
                #                                          {0}             {1}           {2}            {3}            {4}            {5}              {6}            {7}            {8}             {9}           {10}      {11}      {12}
                set-content -path $outputFile -value "SamAccountName,objectClass,DistinguishedName,PasswordLastSet,lastLogon,PasswordNeverExpires,ScriptPath,TrustedForDelegation,HasSPNsAssigned,objectSid,whenCreated,whenChanged,memberOf,UACRisk,StrongAESUsed" -force
                write-host "======Report file located at $outputFile======" -ForegroundColor darkmagenta -BackgroundColor white
            }
            $groupsToQuery = $dDomain.privilegedgroupnames.values[0].split("`n")
            foreach ($groupName in $groupsToQuery) {
                $group = $dDomain.GetObjectByName("$groupName","domain",$false)
                if ($null -eq $group) {
                    $group = $dDomain.GetObjectByName("$groupName","forest",$false)
                }
                $groupDn = $group.properties.distinguishedname
                $groupSid = $($dDomain.GetStringSidFromBytes([byte[]]$($group.properties.objectsid)))
                $searchedGroups += $groupSid
                Get-AdPrivRoleReportWorker -object "$groupDn" -recurse -myDomain $dDomain -outputFile $outputFile -searchedGroups $searchedGroups
            }
        }
        
    } else {
        try {
            # start in domain
            $group = $null
            $group = $myDomain.GetObjectByDn("$object","domain")
            if ($group -eq $null) {
                throw
            }
        }
        catch {
            # fail over to forest
            $group = $myDomain.GetObjectByDn("$object","forest")
        }
        if ($group) {
            if ([string]::IsNullOrEmpty($outputFile)) {
                $outputFile = "{0}\{1}-{2}-{3}.{4}" -f $env:temp, "$($group.properties.name)", $($myDomain.domainNetbiosName),$((get-date).tostring("yyyy-MM-ddTHHmmss")), "csv"
                write-host "======Report file located at $outputFile======" -ForegroundColor darkmagenta -BackgroundColor white
            }
            if (!(Test-Path $outputFile)) {
                set-content -path $outputFile -value "SamAccountName,objectClass,DistinguishedName,PasswordLastSet,lastLogon,PasswordNeverExpires,ScriptPath,TrustedForDelegation,HasSPNsAssigned,objectSid,whenCreated,whenChanged,memberOf" -force
            }
            $groupSid = $($myDomain.GetStringSidFromBytes([byte[]]$($group.properties.objectsid)))
            $searchedGroups += $groupSid
            $group.properties.member | % {
                $objToFind = $_
                try {
                    $adObject = $null
                    $adObject = $myDomain.GetObjectByDn("$objToFind","domain")
                    if ($adObject -eq $null) {
                        throw
                    }
                } catch {
                    $adObject = $myDomain.GetObjectByDn("$objToFind","forest")
                }
                if ($adObject) {
                    Get-AdPrivRoleReportWorker -object "$($adObject.properties.distinguishedname)" -recurse:$recurse -myDomain $myDomain -outputFile $outputFile -searchedGroups $searchedGroups
                }
            }
        }
    }
    if ($OutputToConsole) {
        Get-content $outputFile | ConvertFrom-CSV
    }
}
function Get-AdPrivRoleReportWorker
{
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$true)]
        [string]$object,
        [Parameter(Mandatory=$false)]
        [switch]$recurse,
        [Parameter(Mandatory=$true)]
        $myDomain,
        [Parameter(Mandatory=$true)]
        [string]$outputFile,
        [Parameter(Mandatory=$true)]
        [string[]]$searchedGroups
    )
    try {
        # start in domain
        $providedObject = $null
        $providedObject = $myDomain.GetObjectByDn("$object","domain")
        if ($providedObject -eq $null) {
            throw
        }
    } catch {
        # fail over to forest
        $providedObject = $myDomain.GetObjectByDn("$object","forest")
    }
    if ($providedObject) {
        if (!(Test-Path $outputFile)) {
            set-content -path $outputFile -value "SamAccountName,objectClass,DistinguishedName,PasswordLastSet,lastLogon,PasswordNeverExpires,ScriptPath,TrustedForDelegation,HasSPNsAssigned,objectSid,whenCreated,whenChanged,memberOf,UACRisk,StrongAESUsed" -force
        }
        if ("group" -in $providedObject.Properties.objectclass) {
            $providedObject.properties.member | % {
                $objToFind = $_
                try {
                    $adObject = $null
                    $adObject = $myDomain.GetObjectByDn("$objToFind","domain")
                    if ($adObject -eq $null) {
                        throw
                    }
                } catch {
                    $adObject = $myDomain.GetObjectByDn("$objToFind","forest")
                }
                if ($adObject) {
                    if ("group" -in $adObject.Properties.objectclass -and $recurse) {
                        $groupSid = $($myDomain.GetStringSidFromBytes([byte[]]$($adObject.Properties.objectsid)))
                        if ($groupSid -notin $searchedGroups) {
                            $searchedGroups += $($myDomain.GetStringSidFromBytes([byte[]]$($adObject.Properties.objectsid)))
                            "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12}\{13}" -f $(try { $($adObject.properties.samaccountname) } catch {""}), $(try { $($($adObject.properties.objectclass) -join ":") } catch {""}), $(try { $(($adObject.properties.distinguishedname.tolower().tostring()).replace(',','_')) } catch {""}), "", "", "", "", "", "", $(try { $($myDomain.GetStringSidFromBytes([byte[]]$($adObject.properties.objectsid))) } catch {""}), $(try { $($adObject.properties.whencreated) } catch {""}), $(try { $($adObject.properties.whenchanged) } catch {""}), $myDomain.domainNetbiosName, $(try { $($providedObject.properties.samaccountname) } catch {""}) | add-content -path $outputFile
                            if ($recurse) {
                                Get-AdPrivRoleReportWorker -object "$($adObject.properties.distinguishedname)" -recurse:$recurse -myDomain $myDomain -outputFile $outputFile -searchedGroups $searchedGroups
                            }
                        }
                    } else {
                        Add-AdPrivRoleUserDataToFile -adObject $adObject -parentGroup $($providedObject.properties.samaccountname) -myDomain $myDomain -outputFile $outputFile
                    }
                }
            }
        } else {
            Add-AdPrivRoleUserDataToFile -adObject $adObject -parentGroup $($providedObject.properties.samaccountname) -myDomain $myDomain -outputFile $outputFile
        }
        
    }
}
function Get-AdRiskyAclReport 
{
    ##########################################################################################################
    <#
    .SYNOPSIS
        Get's risky ACL report from AD.
    
    .DESCRIPTION
        This function supports type modes of operation: Standard IR (by means of the -standardIR switch), and
        Single Object (by means of the -object switch). Standard IR mode will check domain DSE, Domain Controllers,
        Domain Admins, Enterprise Admins (if run on forest DC), Schema Admins (if run on forest DC), krbtgt, and
        AdminSDHolder. The function will recurse group membership for nesting checks. The ACL's it checks for are:
        "GenericWrite", "WriteDacl", "WriteOwner", "AllExtendedRights", "GenericAll", "DS-Replication-Get-Changes-All",
        "DS-Replication-Get-Changes", "User-Change-Password", "User-Force-Change-Password", "Member" (this is adding
        yourself to a group you don't own), and "Allowed-To-Authenticate"

        The -object switch takes a DN. Accept this. If you're running this in -object mode and the target is a group, you can pass the -recurse
        switch to recursively check nested members.

        You can filter out the default accounts by using the -filterSafe switch. This will remove entries for
        the following accounts: "NT AUTHORITY\SYSTEM","Enterprise Read-only Domain Controllers",
        "Domain Admins", "Enterprise Admins", "Schema Admins", "Domain Controllers", "NT AUTHORITY\Enterprise Domain Controllers", 
        "BUILTIN\Administrators". This is useful because these groups are **supposed** to have risky ACL's on sensitive
        objects. If you're concerned about these groups then you need to audit their memberships.
        
        The -filterSafe switch can be used in either Standard IR mode or Object mode.

    .EXAMPLE
        Get-AdRiskyAclReport -standardIR -filterSafe

        Run this in a quick mode for the typical persistence checks, without alerting on existing privileged accounts.
    .EXAMPLE
        Get-AdRiskyAclReport -standardIR

        Run this in a quick mode for the typical persistence checks, with alerting on existing privileged accounts.
    .EXAMPLE
        Get-AdRiskyAclReport -object "OU=Domain Controllers,dc=contoso,dc=com"

        Get a detailed audit on the Domain Controllers OU.
    .EXAMPLE
        Get-AdRiskyAclReport -object "CN=Domain Admins,CN=Users,dc=contoso,dc=com"

        Get a detailed audit on the properties of the Domain Admins group.
    .EXAMPLE
        Get-AdRiskyAclReport -object "CN=Domain Admins,CN=Users,dc=contoso,dc=com"

        Get a detailed audit on the properties of the Domain Admins group AND everything in it.

    .OUTPUTS
        Console output

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
    [CmdletBinding(DefaultParameterSetName = 'standardir')]
    Param(
        [Parameter(Mandatory=$false, ParameterSetName='standardir')]
        [switch]$standardIR=$true,
        [Parameter(Mandatory=$true, ParameterSetName='object')]
        [string]$object,
        [Parameter(Mandatory=$false, ParameterSetName='object')]
        [switch]$recurse,
        [Parameter(Mandatory=$false)]
        [switch]$filterSafe,
        [Parameter(DontShow)]
        [System.Collections.Hashtable]$objectTypeGUID,
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )
    if ($objectTypeGUID -eq $null) {
        $objectTypeGUID = Get-AdGuids
    } else {

    }
    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
    $riskyActiveDirectoryRightsAll = @("GenericWrite", "WriteDacl", "WriteOwner", "AllExtendedRights", "GenericAll","WriteProperty")
    $riskyObjectTypesAll = @($(($objectTypeGuid.GetEnumerator() | ? { $_.Value -eq "GP-Link" }).name.guid),$(($objectTypeGuid.GetEnumerator() | ? { $_.Value -eq "Allowed-To-Authenticate" }).name.guid),$(($objectTypeGuid.GetEnumerator() | ? { $_.Value -eq "Member" }).name.guid),$(($objectTypeGuid.GetEnumerator() | ? { $_.Value -eq "DS-Replication-Get-Changes-All" }).name.guid), $(($objectTypeGuid.GetEnumerator() | ? { $_.Value -eq "DS-Replication-Get-Changes" }).name.guid), $(($objectTypeGuid.GetEnumerator() | ? { $_.Value -eq "User-Change-Password" }).name.guid), $(($objectTypeGuid.GetEnumerator() | ? { $_.Value -eq "User-Force-Change-Password" }).name.guid))
    $safeIdentities = @("NT AUTHORITY\SELF","Everyone","NT AUTHORITY\SYSTEM","$($myDomain.forestNetbiosName)\Enterprise Read-only Domain Controllers","$($myDomain.domainNetbiosName)\$($myDomain.privilegedGroupNames["Domain Admins"])", "$($myDomain.forestNetbiosName)\$($myDomain.privilegedGroupNames["Enterprise Admins"])", "$($myDomain.forestNetbiosName)\$($myDomain.privilegedGroupNames["Schema Admins"])", "$($myDomain.domainNetbiosName)\$($myDomain.privilegedGroupNames["Domain Controllers"])", "NT AUTHORITY\Enterprise Domain Controllers", "BUILTIN\Administrators")
    if ($PSCmdlet.ParameterSetName -eq 'standardir') {
        if ($filterSafe) {
            write-warning "You have elected to filter safe identities using the -filterSafe switch. The following identities will NOT be included in the ACL report: $($safeIdentities.split(',') -join ',')"
        }
        $aclCheckLocations = @()
        try {
            $aclCheckLocations += $myDomain.domainDn
        } catch {

        }
        try {
            $aclCheckLocations += $myDomain.GetObjectByFilter("(ou=domain controllers)","domain").properties.distinguishedname
        } catch {

        }
        try {
            $aclCheckLocations += $myDomain.GetObjectByFilter("(sAmAccountName=krbtgt)","domain").properties.distinguishedname
        } catch {

        }
        try {
            $aclCheckLocations += $myDomain.GetObjectBySid("$($myDomain.domainsid)-512","domain").properties.distinguishedname
        } catch {

        }
        try {
            $aclCheckLocations += $myDomain.GetObjectBySid("$($myDomain.forestsid)-519","forest").properties.distinguishedname
        } catch {

        }
        try {
            $aclCheckLocations += $myDomain.GetObjectBySid("$($myDomain.forestsid)-518","forest").properties.distinguishedname
        } catch {

        }
        try {
            $aclCheckLocations += $myDomain.GetObjectByFilter("(cn=adminsdholder)","domain").properties.distinguishedname
        } catch {

        }
        foreach ($aclCheckLocation in $aclCheckLocations){
            if ($aclCheckLocation -ne $null) {
                Get-AdRiskyAclReport -object $aclCheckLocation -filterSafe:$filterSafe -objectTypeGUID $objectTypeGUID -myDomain $myDomain
                $isGroup=$false 
                try {
                    if (($aclCheckLocation).tostring().contains($myDomain.domaindn)) {
                        $objCheck = $myDomain.GetObjectByDn("$aclCheckLocation","domain")
                    } else {
                        $objCheck = $myDomain.GetObjectByDn("$aclCheckLocation","forest")
                    }
                    if ("group" -in $objCheck.properties.objectclass) {
                        $isGroup = $true
                        if ($isGroup) {
                            $groupMembers = $objCheck.properties.member
                        } else {
                            $groupMembers = $null
                        }
                    }
                } catch {
                    
                }
                if ($isGroup) {
                    write-verbose "$aclCheckLocation is a group"

                    $groupMembers | % {
                        if (!([string]::IsNullOrEmpty($_))) {
                            write-verbose "$_ is a member of $(($aclCheckLocation.split(',')[0]).replace('CN=','')). Checking $_ directly"
                            Get-AdRiskyAclReport -object $_ -recurse -filterSafe:$filterSafe -objectTypeGUID $objectTypeGUID -myDomain $myDomain
                        }
                    }
                }
            }
        }
        $outputFile = "{0}\{1}.{2}" -f $env:temp, "AdRiskyAclReport", "CSV"
        write-host "======Report file located at $outputFile======" -ForegroundColor darkmagenta -BackgroundColor white
    } else {
        try {
            $accessList = $null
            $accessList = $myDomain.GetAdAcl("$object","domain")
            if (!($accessList)) {
                $accessList = $myDomain.GetAdAcl("$object","forest")
            }
            if ($accessList) {
                if ($object -like "CN=AdminSDHolder*") {
                    if ($accessList | ? {$_.IsInherited -eq $true}) {
                        write-warning "AdminSDHolder has Inheritence Enabled. THIS SHOULD BE DISABLED!"
                    }
                }
            } else {
                throw
            }
        } catch {
            write-error "Failed to get ACL list for $object"
            throw
        }
        write-verbose "Object mode: Got ACL for $object"
        $accessList | % {
            if ($filterSafe -and ($safeIdentities -contains $_.identityreference)){
            
            } else {
                $aclarray = $_.activedirectoryrights.tostring().replace(' ','').split(',')
                $compare = $null
                $compare = $aclarray | Where-Object -FilterScript { $_ -in $riskyActiveDirectoryRightsAll }
                #($($_.objecttype.guid) -in $riskyObjectTypesAll)
                #($aclarray | Where-Object -FilterScript { $_ -in $riskyActiveDirectoryRightsAll }) -ne $null
                #(($($_.objecttype.guid) -in $riskyObjectTypesAll) -or (($aclarray | Where-Object -FilterScript { $_ -in $riskyActiveDirectoryRightsAll }) -ne $null))
                if (($($_.objecttype.guid) -in $riskyObjectTypesAll) -or (($aclarray | Where-Object -FilterScript { $_ -in $riskyActiveDirectoryRightsAll }) -ne $null)) {
                    $compareObj = [PSCustomObject]@{
                        Location = $object
                        Identity = $_.IdentityReference
                        AccesssRights = ($_.ActiveDirectoryRights) -join ','
                        ExtendedRights = $($objectTypeGuid[[GUID]"$($_.objecttype.guid)"])
                        Inherited = $_.isinherited
                    }
                    try {
                        $stream = [System.IO.StreamWriter]::new( "$env:temp\AdRiskyAclReport.csv",$true )
                        $stream.WriteLine("$($object.replace(',','_')),$($compareObj.Identity),$($compareObj.AccesssRights),$($compareObj.ExtendedRights),$($compareObj.Inherited)")
                    } catch {

                    } finally {
                        if ($stream) {
                            $stream.close()
                        }
                    }
                    #we got risky acls
                    $compareObj
                }
            }
        }
        $isGroup=$false 
        try {
            if (($aclCheckLocation).tostring().contains($myDomain.domaindn)) {
                $objCheck = $myDomain.GetObjectByDn("$object","domain")
            } else {
                $objCheck = $myDomain.GetObjectByDn("$object","forest")
            }
        } catch {
        } 
        if ("group" -in $objCheck.properties.objectclass) {
            $isGroup = $true
        }
        if ($isGroup -and $recurse) {
            $objCheck.properties.member | % {
                if (!([string]::IsNullOrEmpty($_))) {
                    write-verbose "$_ is a member of $(($object.split(',')[0]).replace('CN=','')). Checking $_ directly"
                    Get-AdRiskyAclReport -object $_ -recurse -filterSafe:$filterSafe -objectTypeGUID $objectTypeGUID -myDomain $myDomain
                }
            }
        }
    }
    if ($stream) {
        $stream.close()
    }
}
function Get-AdRiskyGpoReport
{
    ##########################################################################################################
    <#
    .SYNOPSIS
        Gets a report of GPO's in the domain and evaluates them for risk.
    
    .DESCRIPTION
        Get-AdRiskyGpoReport writes a report of GPO's that are condiered risky.
        These would be GPO's that deploy scheduled tasks, modify registry, modify
        environemtn, copy files, modify users and groups, or deploy MSI.

        It also shows the GPO's created or modified in a specific day range.

    .EXAMPLE
        Get-AdRiskyGpoReport -days 15

    .OUTPUTS
        Writes to a JSON file and the screen

    .NOTES
        Author: Will Sykes

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
    
    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [parameter(Mandatory=$false)]
        [int]$days=30,
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )
    
    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
    $gpoCount = (get-gpo -all).count
    if ($gpoCount -lt 2) {
        $gpoWord = "GPO"
    } else {
        $gpoWord = "GPO's"
    }
    write-host "Getting GPO report for $gpoCount $gpoWord"
    if ($gpoCount -gt 100) {
        write-warning "This may take a while."
    }
    [xml]$gpoReport = Get-GPOReport -All -Domain $myDomain.domainFqdn -server $myDomain.chosenDc -ReportType Xml
    #gpos created last days
    $gpoInDayThreshold = (Select-Xml -Xml $gpoReport -XPath '/GPOS').Node.Gpo | ? { ([datetime]($_.CreatedTime) -gt (get-date).adddays(-$days)) -or ([datetime]($_.ModifiedTime) -gt (get-date).adddays(-$days))}
    write-host "GPO's created or modified within the last $days days" -ForegroundColor cyan
    if ($gpoInDayThreshold) {
        $gpoInDayThreshold  | select Name,CreatedTime,ModifiedTime | ft
    } else {
        write-host "No GPO's created of modified within the last $days days"
    }
    
    $riskyGpoReport = (Select-Xml -Xml $gpoReport -XPath '/GPOS').Node.Gpo | ? { ("Files" -in $_.Computer.ExtensionData.Name) -or ("Scheduled Tasks" -in $_.Computer.ExtensionData.Name) -or ("Environment Variables" -in $_.Computer.ExtensionData.Name) -or ("Windows Registry" -in $_.Computer.ExtensionData.Name) -or ("Software Installation" -in $_.Computer.ExtensionData.Name) -or ("Local Users and Groups" -in $_.Computer.ExtensionData.Name) }
    # we now have a list of risky gpo's, but why are they risky?
    $riskyGpoList = [System.Collections.Generic.List[AdGpo]]::new()
    foreach ($riskyGpo in $riskyGpoReport) {
        #build the object to add the the list
        $newRiskyGpo = [AdGpo]::new($riskyGpo.name, $true, [System.Array]::new,$riskyGpo.CreatedTime,$riskyGpo.ModifiedTime,[System.Collections.Hashtable]::new())
        $newRiskyGpo.gpoGuid = $riskyGpo.Identifier.Identifier.'#text'
        #does it deploy scheduled tasks?
        if ($riskyGpo | ? { ("Scheduled Tasks" -in $_.Computer.ExtensionData.Name) }) {
            if ($riskyGpo.Computer.ExtensionData.Extension.ScheduledTasks.ImmediateTaskV2) {
                foreach ($immediateTask in $riskyGpo.Computer.ExtensionData.Extension.ScheduledTasks.ImmediateTaskV2) { 
                    $immediateTask.properties.Task.Actions | % {
                        try {
                            #$newRiskyGpo.riskReasons["scheduledtask-$($immediateTask.name)"]="$($_.Exec.Command)$($_.Exec.Arguments)" 
                            $newRiskyGpo.riskReasons += [AdGpoRiskDetail]::new("scheduledtask",$($immediateTask.name),"$($_.Exec.Command) $($_.Exec.Arguments)")
                        } catch {}
                    }
                }
            }
            if ($riskyGpo.Computer.ExtensionData.Extension.ScheduledTasks.TaskV2) {
                foreach ($stdTask in $riskyGpo.Computer.ExtensionData.Extension.ScheduledTasks.TaskV2) { 
                    $stdTask.properties.Task.Actions | % {
                        try {
                            $newRiskyGpo.riskReasons += [AdGpoRiskDetail]::new("scheduledtask",$($stdTask.name),"$($_.Exec.Command) $($_.Exec.Arguments)")
                        } catch {}
                    }
                }
            }
            $newRiskyGpo.deploysSchedTask = $true
        }
        #does it deploy files?
        if ($riskyGpo | ? { ("Files" -in $_.Computer.ExtensionData.Name)}) {
            $newRiskyGpo.deploysFiles = $true
            foreach ($file in $riskyGpo.Computer.ExtensionData.Extension.FilesSettings.File) {
                try {
                    $newRiskyGpo.riskReasons += [AdGpoRiskDetail]::new("filedeploy",$($file.name),$file.Properties.targetPath)
                } catch {}
            }
        }
        #does it mod environment
        if ($riskyGpo | ? {"Environment Variables" -in $_.Computer.ExtensionData.Name}) {
            $newRiskyGpo.modsEnvironment = $true
            foreach ($envVar in $riskyGpo.Computer.ExtensionData.Extension.EnvironmentVariables.EnvironmentVariable) {
                try {
                    $newRiskyGpo.riskReasons += [AdGpoRiskDetail]::new("environmentVariable",$($envVar.name),$envVar.status.split('=')[1])
                } catch {}
            }
        }
        #does it mod reg
        if ($riskyGpo | ? {"Windows Registry" -in $_.Computer.ExtensionData.Name}) {
            $newRiskyGpo.modsReg = $true
            foreach ($regMod in $riskyGpo.Computer.ExtensionData.Extension.RegistrySettings.Registry.Properties) {
                try {
                    $newRiskyGpo.riskReasons += [AdGpoRiskDetail]::new("registry","$($regMod.Key)\$($regMod.Name)",$regMod.Value)
                } catch {}
            }
        }
        #does it deploy software
        if ($riskyGpo | ? {"Software Installation" -in $_.Computer.ExtensionData.Name}) {
            $newRiskyGpo.deploysSoftware = $true
            foreach ($swDeploy in $riskyGpo.Computer.ExtensionData.Extension.MsiApplication) {
                try {
                    if (!([string]::IsNullOrEmpty($swDeploy.name))) {
                        $newRiskyGpo.riskReasons += [AdGpoRiskDetail]::new("swdeploy",$($swDeploy.name),$swDeploy.path)
                    }
                } catch {}
            }
        }
        #does it mod lugs
        if ($riskyGpo | ? {"Local Users and Groups" -in $_.Computer.ExtensionData.Name}) {
            $newRiskyGpo.modsLocalUG = $true
            foreach ($modGroup in $riskyGpo.Computer.ExtensionData.Extension.LocalUsersAndGroups.Group.Properties) {
                    foreach ($modMember in $riskyGpo.Computer.ExtensionData.Extension.LocalUsersAndGroups.Group.Properties.Members.Member) {
                        try {
                            $newRiskyGpo.riskReasons += [AdGpoRiskDetail]::new("moddedgroup",$($modGroup.groupName),"$($modMember.name):$($modMember.action)")
                        } catch {}
                    }
            }
        }
        #get link paths #get enabled
        foreach ($linkTo in $riskyGpo.LinksTo) {
            $newRiskyGpo.linkLocations[$linkTo.SOMPath]=$linkTo.enabled
        }

        $riskyGpoList.Add($newRiskyGpo)
    }
    if ($riskyGpoList.count -gt 0) {
        write-host "Potentially risky GPO's" -ForegroundColor cyan
        $riskyGpoList | sort-object -property gpoName | ft gpoName,isRisky,deploysSchedTask,deploysFiles,deploysSoftware,modsReg,modsLocalUG,modsEnvironment,linkLocations -AutoSize
        # dump riskygpolist to json somewhere for further review
        $riskyGpoList | convertto-json -depth 9 | out-file $env:temp\riskyGpoReport.json -force
        write-host "======Report file located at $env:temp\riskyGpoReport.json======" -ForegroundColor darkmagenta -BackgroundColor white
    } else {
        write-host "No potentially risky GPO's found"
    }
    
}
function Get-AdRiskySysvolReport {
    ##########################################################################################################
    <#
    .SYNOPSIS
        Get's a list of executables in SYSVOL and returns a report
    
    .DESCRIPTION
        Get-AdRiskySysvolReport writes a report of executable files in SYSVOL
        and outputs their information, including SHA256 hash, to the screen
        and to a CSV file.

    .EXAMPLE
        Get-AdRiskySysvolReport

    .OUTPUTS
        Writes to a CSV file and the screen

    .NOTES
        Author: Will Sykes

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


    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )
    
    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
    $sysvolFiles = Get-ChildItem -Recurse -Path "$('\\{0}\SYSVOL\{0}' -f $myDomain.domainFqdn)" -Include "*.exe","*.com","*.bat","*.cmd", "*.js", "*.vbs", "*.ps1"
    $reportArray = @()
    foreach ($file in $sysvolFiles) {
        $hash = $file | get-filehash -algorithm sha256
        $reportArray += [PSCustomObject]@{
            Name = $file.Name
            Path = $(if ($hash) {$hash.path | Split-Path -Parent} else {$null})
            CreationTime = $file.CreationTime
            LastWriteTime = $file.LastWriteTime
            Sha256 = $(if ($hash) {$hash.hash} else {$null})
        }
    }
    $sortedReportArray = $reportArray | Sort-Object -Property creationtime -descending
    $sortedReportArray | select name,path,creationtime,lastwritetime,sha256
    $sortedReportArray | ConvertTo-Csv -Delimiter ',' -NoTypeInformation | out-file $env:temp\riskySysvolReport.csv -force
    write-host ""
    write-host "======Report file located at $env:temp\riskySysvolReport.csv======" -ForegroundColor darkmagenta -BackgroundColor white
}
function Get-AdRiskyUacReport {
    [CmdletBinding()]
    Param(
        [parameter(Mandatory=$True,Position=1)]
        [String[]]$uacDecoded,
        [parameter(Mandatory=$True,Position=2)]
        [String]$separator,
        [parameter(Mandatory=$false,Position=3)]
        [String[]]$whatIsRisky=@("DONT_REQ_PREAUTH","ENCRYPTED_TEXT_PWD_ALLOWED","PASSWD_NOTREQD","USE_DES_KEY_ONLY","TRUSTED_TO_AUTH_FOR_DELEGATION","TRUSTED_FOR_DELEGATION","DONT_EXPIRE_PASSWORD")
    )
    ($uacDecoded | Where-Object -FilterScript { $_ -in $whatIsRisky }) -join "$separator"
}
function Get-AzureAdCAPolicies {
    ##########################################################################################################
    <#
    .SYNOPSIS
        Connects to an azure AD tenant and pulls conditional access policies.
        Connects to the tenant for the user that it auths as.
    
    .DESCRIPTION
        Get-AzureAdCAPolicies writes the output to the specified path.

    .EXAMPLE
        Get-AzureAdCAPolicies -FolderPath c:\temp -tenantId "mytenantid"

    .OUTPUTS
        Writes to a CSV file

    .NOTES
        Author: Least Privilege Design and Implementation

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
    param (
        [Parameter(Mandatory=$false)]
        [string]$FolderPath=$env:temp,
        [Parameter(Mandatory=$true)]
        [string]$tenantId
    )
    Connect-Mg -tenantId $tenantId | out-null
    $CAPolicies=Get-MgIdentityConditionalAccessPolicy

    # Specify the name of the record type that you'll be creating
    $LogType = "DashboardAAD_CA"
    $DataArr = @()
    $x=1
    $Total = $CAPolicies.Count
    $AllAADRoles = Get-MgDirectoryRoleTemplate

    foreach ($CAPolicy in $CAPolicies) {

            Write-Progress -Id 1 -Activity "Process Conditional Access Policies" -Status ("Checked {0}/{1} CA Policy" -f $x++, $Total) -PercentComplete ((($x-1) / $Total) * 100)
            $DataObj = New-Object -TypeName PSObject
            
            $DataObj | Add-Member -MemberType NoteProperty -Name Id -force -Value $CAPolicy.id
            $DataObj | Add-Member -MemberType NoteProperty -Name DisplayName -force -Value $CAPolicy.displayName
            $DataObj | Add-Member -MemberType NoteProperty -Name CreatedDateTime -force -Value ""
            $DataObj | Add-Member -MemberType NoteProperty -Name ModifiedDateTime -force -Value ""
            $DataObj | Add-Member -MemberType NoteProperty -Name State -force -Value $CAPolicy.State

            $Details = $null
            $CAData = $CAPolicy.Conditions.SignInRiskLevels
            for ($i=0;$i -lt $CAData.Count;++$i){
                if (($i+1) -ne $CAData.Count) { 
                    $Details += "$($CAData[$i])`r`n"
                }
                else {
                    $Details += "$($CAData[$i])"    
                }
            }
            $DataObj | Add-Member -MemberType NoteProperty -Name SignInRiskLevels -force -Value $Details

        
            $Details = $null
            $CAData = $CAPolicy.Conditions.UserRiskLevels
            for ($i=0;$i -lt $CAData.Count;++$i){
                if (($i+1) -ne $CAData.Count) { 
                    $Details += "$($CAData[$i])`r`n"
                }
                else {
                    $Details += "$($CAData[$i])"    
                }
            }
            $DataObj | Add-Member -MemberType NoteProperty -Name UserRiskLevels -force -Value $Details
        
            $Details = $null
            $CAData = $CAPolicy.Conditions.Users.IncludeGroups
            for ($i=0;$i -lt $CAData.Count;++$i){
                if ($CAData[$i] -match '(?im)^[{(]?[0-9A-F]{8}[-]?(?:[0-9A-F]{4}[-]?){3}[0-9A-F]{12}[)}]?$') {
                    $filterString = "Id eq '"+$CAData[$i]+"'"
                    $Name = $(Get-MgGroup -Filter $filterString).displayname
                }
                else {
                    $Name = $CAData[$i]
                }
            
                if (($i+1) -ne $CAData.Count) { 
                    $Details += "$Name`r`n"
                }
                else {
                    $Details += "$Name"    
                }
            }
            $DataObj | Add-Member -MemberType NoteProperty -Name IncludeGroups -force -Value $Details

            $Details = $null
            $CAData = $CAPolicy.Conditions.Users.ExcludeGroups
            for ($i=0;$i -lt $CAData.Count;++$i){
                            if ($CAData[$i] -match '(?im)^[{(]?[0-9A-F]{8}[-]?(?:[0-9A-F]{4}[-]?){3}[0-9A-F]{12}[)}]?$') {
                    $filterString = "Id eq '"+$CAData[$i]+"'"
                    $Name = $(Get-MgGroup -Filter $filterString).displayname
                }
                else {
                    $Name = $CAData[$i]
                }
            
                if (($i+1) -ne $CAData.Count) { 
                    $Details += "$Name`r`n"
                }
                else {
                    $Details += "$Name"    
                }
            }
            $DataObj | Add-Member -MemberType NoteProperty -Name ExcludeGroups -force -Value $Details
      
            $Details = $null
            $CAData = $CAPolicy.Conditions.Users.IncludeRoles
            for ($i=0;$i -lt $CAData.Count;++$i){
                if ($CAData[$i] -match '(?im)^[{(]?[0-9A-F]{8}[-]?(?:[0-9A-F]{4}[-]?){3}[0-9A-F]{12}[)}]?$') {
                    $Name = $($AllAADRoles | Where-Object {$_.objectid -eq $CAData[$i]}).DisplayName
                }
                else {
                    $Name = $CAData[$i]
                }
            
                if (($i+1) -ne $CAData.Count) { 
                    $Details += "$Name`r`n"
                }
                else {
                    $Details += "$Name"    
                }
            }
            $DataObj | Add-Member -MemberType NoteProperty -Name IncludeRoles -force -Value $Details
                
            $Details = $null
            $CAData = $CAPolicy.Conditions.Users.ExcludeRoles
            for ($i=0;$i -lt $CAData.Count;++$i){
                if ($CAData[$i] -match '(?im)^[{(]?[0-9A-F]{8}[-]?(?:[0-9A-F]{4}[-]?){3}[0-9A-F]{12}[)}]?$') {
                    $Name = $($AllAADRoles | Where-Object {$_.objectid -eq $CAData[$i]}).DisplayName
                }
                else {
                    $Name = $CAData[$i]
                }
            
                if (($i+1) -ne $CAData.Count) { 
                    $Details += "$Name`r`n"
                }
                else {
                    $Details += "$Name"    
                }
            }
            $DataObj | Add-Member -MemberType NoteProperty -Name ExcludeRoles -force -Value $Details
        
            $Details = $null
            $CAData = $CAPolicy.Conditions.Users.IncludeUsers
            for ($i=0;$i -lt $CAData.Count;++$i){
                if ($CAData[$i] -match '(?im)^[{(]?[0-9A-F]{8}[-]?(?:[0-9A-F]{4}[-]?){3}[0-9A-F]{12}[)}]?$') {
                    $filterString = "Id eq '"+$CAData[$i]+"'"
                    $Name = $(Get-MgUser -Filter $filterString).UserPrincipalName
                }
                else {
                    $Name = $CAData[$i]
                }
            
                if (($i+1) -ne $CAData.Count) { 
                    $Details += "$Name`r`n"
                }
                else {
                    $Details += "$Name"    
                }
            }
            $DataObj | Add-Member -MemberType NoteProperty -Name IncludeUsers -force -Value $Details
               
            $Details = $null
            $CAData = $CAPolicy.Conditions.Users.ExcludeUsers
            for ($i=0;$i -lt $CAData.Count;++$i){
                if ($CAData[$i] -match '(?im)^[{(]?[0-9A-F]{8}[-]?(?:[0-9A-F]{4}[-]?){3}[0-9A-F]{12}[)}]?$') {
                    $filterString = "Id eq '"+$CAData[$i]+"'"
                    $Name = $(Get-MgUser -Filter $filterString).UserPrincipalName
                }
                else {
                    $Name = $CAData[$i]
                }
            
                if (($i+1) -ne $CAData.Count) { 
                    $Details += "$Name`r`n"
                }
                else {
                    $Details += "$Name"    
                }
            }
            $DataObj | Add-Member -MemberType NoteProperty -Name ExcludeUsers -force -Value $Details
         
            $Details = $null
            $CAData = $CAPolicy.Conditions.Applications.IncludeApplications
            for ($i=0;$i -lt $CAData.Count;++$i){
            
                if ($CAData[$i] -match '(?im)^[{(]?[0-9A-F]{8}[-]?(?:[0-9A-F]{4}[-]?){3}[0-9A-F]{12}[)}]?$') {
                    $Name = $(Get-MgServicePrincipal -Filter ("appId eq '{0}'" -f $CAData[$i])).displayname
                }
                else {
                    $Name = $CAData[$i]
                }
            
            
                if (($i+1) -ne $CAData.Count) {
                    $Details += "$Name`r`n"
                }
                else {
                    $Details += "$Name"    
                }
                $name =$null
            }
            $DataObj | Add-Member -MemberType NoteProperty -Name IncludeApplications -force -Value $Details
        
            $Details = $null
            $CAData = $CAPolicy.Conditions.Applications.ExcludeApplications
            for ($i=0;$i -lt $CAData.Count;++$i){
                if ($CAData[$i] -match '(?im)^[{(]?[0-9A-F]{8}[-]?(?:[0-9A-F]{4}[-]?){3}[0-9A-F]{12}[)}]?$') {
                    $Name = $(Get-MgServicePrincipal -Filter ("appId eq '{0}'" -f $CAData[$i])).displayname
                }
                else {
                    $Name = $CAData[$i]
                }
            
            
                if (($i+1) -ne $CAData.Count) {
                    $Details += "$Name`r`n"
                }
                else {
                    $Details += "$Name"    
                }
                $name =$null
            }
            $DataObj | Add-Member -MemberType NoteProperty -Name ExcludeApplications -force -Value $Details

            $Details = $null
            $CAData = $CAPolicy.Conditions.Applications.IncludeProtectionLevels
            for ($i=0;$i -lt $CAData.Count;++$i){
                if (($i+1) -ne $CAData.Count) { 
                    $Details += "$($CAData[$i])`r`n"
                }
                else {
                    $Details += "$($CAData[$i])"    
                }
            }
            $DataObj | Add-Member -MemberType NoteProperty -Name IncludeProtectionLevels -force -Value $Details
                
            $Details = $null
            $CAData = $CAPolicy.Conditions.Applications.IncludeUserActions
            for ($i=0;$i -lt $CAData.Count;++$i){
                if ($CAData[$i] -eq "urn:user:registerdevice") {
                    $IncludeUserActions = "Register or join devices"    
                }
                elseif ($CAData[$i] -eq "urn:user:registersecurityinfo") {
                    $IncludeUserActions = "Register security information"
                }
                else {
                    $IncludeUserActions = $CAData[$i]
                }
                $Details += "$IncludeUserActions`r`n"
            }
            $DataObj | Add-Member -MemberType NoteProperty -Name IncludeUserActions -force -Value $Details
                                
            $Details = $null
            $CAData = $CAPolicy.Conditions.Devices.IncludeDeviceStates
            for ($i=0;$i -lt $CAData.Count;++$i){
                if (($i+1) -ne $CAData.Count) { 
                    $Details += "$($CAData[$i])`r`n"
                }
                else {
                    $Details += "$($CAData[$i])"    
                }
            }
            $DataObj | Add-Member -MemberType NoteProperty -Name IncludeDeviceStates -force -Value $Details
                
            $Details = $null
            $CAData = $CAPolicy.Conditions.Devices.ExcludeDeviceStates
            for ($i=0;$i -lt $CAData.Count;++$i){
                if (($i+1) -ne $CAData.Count) { 
                    $Details += "$($CAData[$i])`r`n"
                }
                else {
                    $Details += "$($CAData[$i])"    
                }
            }
            $DataObj | Add-Member -MemberType NoteProperty -Name ExcludeDeviceStates -force -Value $Details
                
            $Details = $null
            $CAData = $CAPolicy.Conditions.deviceStates
            for ($i=0;$i -lt $CAData.Count;++$i){
                if (($i+1) -ne $CAData.Count) { 
                    $Details += "$($CAData[$i])`r`n"
                }
                else {
                    $Details += "$($CAData[$i])"    
                }
            }
            $DataObj | Add-Member -MemberType NoteProperty -Name deviceStates -force -Value $Details
                
            $Details = $null
            $CAData = $CAPolicy.Conditions.Locations.includeLocations
            for ($i=0;$i -lt $CAData.Count;++$i){
                if ($CAData[$i] -match '(?im)^[{(]?[0-9A-F]{8}[-]?(?:[0-9A-F]{4}[-]?){3}[0-9A-F]{12}[)}]?$') {
                    $filterString = "Id eq '"+$CAData[$i]+"'"
                    $Name = $(Get-MgIdentityConditionalAccessNamedLocation -Filter $filterString).DisplayName
                }
                else {
                    $Name = $CAData[$i]
                }
            
                if (($i+1) -ne $CAData.Count) { 
                    $Details += "$Name`r`n"
                }
                else {
                    $Details += "$Name"    
                }
            }
            $DataObj | Add-Member -MemberType NoteProperty -Name includeLocations -force -Value $Details
                
            $Details = $null
            $CAData = $CAPolicy.Conditions.Locations.excludeLocations
            for ($i=0;$i -lt $CAData.Count;++$i){
                if ($CAData[$i] -match '(?im)^[{(]?[0-9A-F]{8}[-]?(?:[0-9A-F]{4}[-]?){3}[0-9A-F]{12}[)}]?$') {
                    $filterString = "Id eq '"+$CAData[$i]+"'"
                    $Name = $(Get-MgIdentityConditionalAccessNamedLocation -Filter $filterString).DisplayName
                }
                else {
                    $Name = $CAData[$i]
                }
            
                if (($i+1) -ne $CAData.Count) { 
                    $Details += "$Name`r`n"
                }
                else {
                    $Details += "$Name"    
                }
            }
            $DataObj | Add-Member -MemberType NoteProperty -Name excludeLocations -force -Value $Details
        
            $Details = $null
            $CAData = $CAPolicy.Conditions.ClientAppTypes
            for ($i=0;$i -lt $CAData.Count;++$i){
                if (($i+1) -ne $CAData.Count) { 
                    $Details += "$($CAData[$i])`r`n"
                }
                else {
                    $Details += "$($CAData[$i])"    
                }
            }
            $DataObj | Add-Member -MemberType NoteProperty -Name ClientAppTypes -force -Value $Details
            
            $Details = $null
            $CAData = $CAPolicy.Conditions.Platforms.includePlatforms
            for ($i=0;$i -lt $CAData.Count;++$i){
                if (($i+1) -ne $CAData.Count) { 
                    $Details += "$($CAData[$i])`r`n"
                }
                else {
                    $Details += "$($CAData[$i])"    
                }
            }
            $DataObj | Add-Member -MemberType NoteProperty -Name includePlatforms -force -Value $Details
                
            $Details = $null
            $CAData = $CAPolicy.Conditions.Platforms.excludePlatforms
            for ($i=0;$i -lt $CAData.Count;++$i){
                if (($i+1) -ne $CAData.Count) { 
                    $Details += "$($CAData[$i])`r`n"
                }
                else {
                    $Details += "$($CAData[$i])"    
                }
            }
            $DataObj | Add-Member -MemberType NoteProperty -Name excludePlatforms -force -Value $Details

            $DataObj | Add-Member -MemberType NoteProperty -Name GrantControls_Operator -force -Value $CAPolicy.GrantControls._Operator
                
            $Details = $null
            $CAData = $CAPolicy.GrantControls.BuiltInControls
            for ($i=0;$i -lt $CAData.Count;++$i){
                if (($i+1) -ne $CAData.Count) { 
                    $Details += "$($CAData[$i])`r`n"
                }
                else {
                    $Details += "$($CAData[$i])"    
                }
            }
            $DataObj | Add-Member -MemberType NoteProperty -Name GrantControls_BuiltInControls -force -Value $Details
                
            $Details = $null
            $CAData = $CAPolicy.GrantControls.CustomAuthenticationFactors
            for ($i=0;$i -lt $CAData.Count;++$i){
                if (($i+1) -ne $CAData.Count) { 
                    $Details += "$($CAData[$i])`r`n"
                }
                else {
                    $Details += "$($CAData[$i])"    
                }
            }
            $DataObj | Add-Member -MemberType NoteProperty -Name CustomAuthenticationFactors -force -Value $Details
                
            $Details = $null
            $CAData = $CAPolicy.GrantControls.TermsOfUse
            for ($i=0;$i -lt $CAData.Count;++$i){
                if (($i+1) -ne $CAData.Count) { 
                    $Details += "$($CAData[$i])`r`n"
                }
                else {
                    $Details += "$($CAData[$i])"    
                }
            }
            $DataObj | Add-Member -MemberType NoteProperty -Name TermsOfUse -force -Value $Details

                
            $Details = $null
            $CAData = $CAPolicy.SessionControls.ApplicationEnforcedRestrictions
            for ($i=0;$i -lt $CAData.Count;++$i){
                if (($i+1) -ne $CAData.Count) { 
                    $Details += "$($CAData[$i])`r`n"
                }
                else {
                    $Details += "$($CAData[$i])"    
                }
            }
            $DataObj | Add-Member -MemberType NoteProperty -Name ApplicationEnforcedRestrictions -force -Value $Details
            
            $DataObj | Add-Member -MemberType NoteProperty -Name cloudAppSecurityType -force -Value $CAPolicy.SessionControls.CloudAppSecurity.cloudAppSecurityType
            $DataObj | Add-Member -MemberType NoteProperty -Name CloudAppSecurity_isEnabled -force -Value $CAPolicy.SessionControls.CloudAppSecurity.isEnabled
            $DataObj | Add-Member -MemberType NoteProperty -Name PersistentBrowser_mode -force -Value $CAPolicy.SessionControls.PersistentBrowser.Mode
            $DataObj | Add-Member -MemberType NoteProperty -Name PersistentBrowser_IsEnabled -force -Value $CAPolicy.SessionControls.PersistentBrowser.IsEnabled
            $DataObj | Add-Member -MemberType NoteProperty -Name SignInFrequency_value -force -Value $CAPolicy.SessionControls.SignInFrequency.value
            $DataObj | Add-Member -MemberType NoteProperty -Name SignInFrequency_type -force -Value $CAPolicy.SessionControls.SignInFrequency.type
            $DataObj | Add-Member -MemberType NoteProperty -Name SignInFrequency_isEnabled -force -Value $CAPolicy.SessionControls.SignInFrequency.isEnabled
            $DataObj | Add-Member -MemberType NoteProperty -Name cTimeStampField -force -Value $TimeStampField
            $DataArr += $DataObj
    }

    
    $ExportFilename = "$FolderPath\CAPolicies.csv"
    Clear-Content $ExportFilename -Force -ErrorAction SilentlyContinue
    $header = '"Id","DisplayName","CreatedDateTime","ModifiedDateTime","State","SignInRiskLevels","UserRiskLevels","IncludeGroups","ExcludeGroups","IncludeRoles","ExcludeRoles","IncludeUsers","ExcludeUsers","IncludeApplications","ExcludeApplications","IncludeProtectionLevels","IncludeUserActions","IncludeDeviceStates","ExcludeDeviceStates","deviceStates","includeLocations","excludeLocations","ClientAppTypes","includePlatforms","excludePlatforms","GrantControls._Operator","GrantControls.BuiltInControls","CustomAuthenticationFactors","TermsOfUse","ApplicationEnforcedRestrictions","cloudAppSecurityType","CloudAppSecurity.isEnabled","PersistentBrowser.mode","PersistentBrowser.IsEnabled","SignInFrequency.value","SignInFrequency.type","SignInFrequency.isEnabled","cTimeStampField","TimeGenerated"'
    $header | Out-File $ExportFilename
    if ($DataArr) {
        $DataArr | Export-Csv -Path $FolderPath\CAPolicies.csv -Delimiter "," -NoTypeInformation -Force
    }
    write-host "======Report file located at $ExportFilename======" -ForegroundColor darkmagenta -BackgroundColor white
}
function Get-AzureAdPrivObjects 
{
    ##########################################################################################################
    <#
    .SYNOPSIS
        Gets accounts that are directly assigned to Azure AD privileged roles
    
    .DESCRIPTION
        Run Get-AzureAdPrivObjects and it will seek and return the collection of users 
        and also save a JSON file for review.

    .EXAMPLE
        Get-AzureAdPrivObjects -tenantId "my-tenant-id"

        Get a report of all users including service principals
    .EXAMPLE
        Get-AzureAdPrivObjects -tenantId "my-tenant-id" -usersOnly

        Get a report of all users only
    .EXAMPLE
        Get-AzureAdPrivObjects -tenantId "my-tenant-id" -saveReport

        Get a report of all users including service principals and save it as a JSON
    .OUTPUTS
        Writes to console

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
    Param(
        [Parameter(Mandatory=$false)]
        [switch]$usersOnly,
        [Parameter(Mandatory=$false)]
        [switch]$saveReport,
        [Parameter(Mandatory=$true)]
        [string]$tenantId
    )
    Connect-Mg -tenantId $tenantId | out-null
    $objList = [System.Collections.Generic.List[AzureObject]]::new()
    $roleMasterList = [System.Collections.Generic.List[AzureAdRole]]::new()
    $privRoles = Get-MgDirectoryRole | ? {$_.DisplayName -like "*Administrator" }
    $privRoles | % {
        $role = [AzureAdRole]::new($($_.Id),$($_.DisplayName))
        $role.objDetails = [System.Collections.Generic.List[AzureObject]]::new()
        $roleMasterList.Add($role)
        foreach ($member in Get-MgDirectoryRoleMember -DirectoryRoleId $role.objId) {
            $obj = Get-MgDirectoryObjectById -Ids $member.Id
            $roleMember = [AzureObject]::new($obj.Id,$obj.AdditionalProperties.userPrincipalName,$obj.AdditionalProperties.displayName,($obj.AdditionalProperties.'@odata.type').split('.')[2],$role.DisplayName)
            $objList.Add($roleMember)
            if ($usersOnly) {
                if ($obj.ObjectType -eq 'User') {
                    $roleMasterList[$roleMasterList.FindIndex({param($x) $x.objId -eq $role.objId})].objDetails.Add($roleMember)
                }
            } else {
                $roleMasterList[$roleMasterList.FindIndex({param($x) $x.objId -eq $role.objId})].objDetails.Add($roleMember)
            }
        }
    }
    
    if ($usersOnly)
    {
        $userList = $objList.FindAll({param($x) $x.objType -eq 'user'})
    }
    if ($saveReport)
    {
        if ($usersOnly) {
            $roleMasterList | convertto-json -depth 9 | out-file $env:temp\allAzurePrivUsers.json -force
            write-host "======Report file located at $env:temp\allAzurePrivUsers.json======" -ForegroundColor darkmagenta -BackgroundColor white
        } else {
            $roleMasterList | convertto-json -depth 9 | out-file $env:temp\allAzurePrivObjects.json -force
            write-host "======Report file located at $env:temp\allAzurePrivObjects.json======" -ForegroundColor darkmagenta -BackgroundColor white
        }
    } else {
        if ($usersOnly) {
            return $userList | sort -Unique -Property objid
        } else {
            return $objList | sort -Unique -Property objid
        }
    }
}
function Get-AzureAdRiskyApps
{
    ##########################################################################################################
    <#
    .SYNOPSIS
        Connects to an azure AD tenant and pulls all apps and checks for risky API grants.
        Connects to the tenant for the user that it auths as.
    
    .DESCRIPTION
        Get-AzureAdRiskyApps writes the output to the screen and json report.

    .EXAMPLE
        Get-AzureAdRiskyApps -tenantId "mytenantid"

    .OUTPUTS
        Writes to the screen and JSON file

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
    #https://m365internals.com/2021/07/24/everything-about-service-principals-applications-and-api-permissions/
    ### https://matthewdavis111.com/msgraph/azure-ad-permission-details/
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$true)]
        [string]$tenantId
    )
    write-warning "This may take some time..."
    Connect-Mg -tenantId $tenantId | out-null
    $riskyPermissions = @{
        "810c84a8-4a9e-49e6-bf7d-12d183f40d01" = "Mail.Read"
        "570282fd-fa5c-430d-a7fd-fc8dc98a9dca" = "Mail.Read"
        "e2a3a72e-5f79-4c64-b1b1-878b674786c9" = "Mail.ReadWrite"
        "024d486e-b451-40bb-833d-3e66d98c5c73" = "Mail.ReadWrite"
        "b633e1c5-b582-4048-a93e-9f11b44c7e96" = "Mail.Send"
        "e383f46e-2787-4529-855e-0e479a3ffac0" = "Mail.Send"
        "d56682ec-c09e-4743-aaf4-1a3aac4caa21" = "Contacts.ReadWrite"
        "6918b873-d17a-4dc1-b314-35f528134491" = "Contacts.ReadWrite"
        "ff74d97f-43af-4b68-9f2a-b77ee6968c5d" = "Contacts.Read"
        "089fe4d0-434a-44c5-8827-41ba8a0b17f5" = "Contacts.Read"
        "6931bccd-447a-43d1-b442-00a195474933" = "MailboxSettings.ReadWrite"
        "818c620a-27a9-40bd-a6a5-d96f7d610b4b" = "MailboxSettings.ReadWrite"
        "40f97065-369a-49f4-947c-6a255697ae91" = "MailboxSettings.Read"
        "87f447af-9fa4-4c32-9dfa-4a57a73d18ce" = "MailboxSettings.Read"
        "b89f9189-71a5-4e70-b041-9887f0bc7e4a" = "People.Read.All"
        "b528084d-ad10-4598-8b93-929746b4d7d6" = "People.Read.All"
        "ba47897c-39ec-4d83-8086-ee8256fa737d" = "People.Read"
        "10465720-29dd-4523-a11a-6a75c743c9d9" = "Files.Read"
        "01d4889c-1287-42c6-ac1f-5d1e02578ef6" = "Files.Read.All"
        "df85f4d6-205c-4ac5-a5ea-6bf408dba283" = "Files.Read.All"
        "5447fe39-cb82-4c1a-b977-520e67e724eb" = "Files.Read.Selected"
        "5c28f0bf-8a70-41f1-8ab2-9032436ddb65" = "Files.ReadWrite"
        "863451e7-0667-486c-a5d6-d135439485f0" = "Files.ReadWrite.All"
        "75359482-378d-4052-8f01-80520e7db3cd" = "Files.ReadWrite.All"
        "8019c312-3263-48e6-825e-2b833497195b" = "Files.ReadWrite.AppFolder"
        "17dde5bd-8c17-420f-a486-969730c1b827" = "Files.ReadWrite.Selected"
        "9d822255-d64d-4b7a-afdb-833b9a97ed02" = "Notes.Create"
        "371361e4-b9e2-4a3f-8315-2a301a3b0a3d" = "Notes.Read"
        "dfabfca6-ee36-4db2-8208-7a28381419b3" = "Notes.Read.All"
        "3aeca27b-ee3a-4c2b-8ded-80376e2134a4" = "Notes.Read.All"
        "615e26af-c38a-4150-ae3e-c3b0d4cb1d6a" = "Notes.ReadWrite"
        "64ac0503-b4fa-45d9-b544-71a463f05da0" = "Notes.ReadWrite.All"
        "0c458cef-11f3-48c2-a568-c66751c238c0" = "Notes.ReadWrite.All"
        "ed68249d-017c-4df5-9113-e684c7f8760b" = "Notes.ReadWrite.CreatedByApp"
        "0e263e50-5827-48a4-b97c-d940288653c7" = "Directory.AccessAsUser.All"
        "1bfefb4e-e0b5-418b-a88f-73c46d2cc8e9" = "Application.ReadWrite.All"
        "bdfbf15f-ee85-4955-8675-146e8e5296b5" = "Application.ReadWrite.All"
        "7ab1d382-f21e-4acd-a863-ba3e13f7da61" = "Directory.ReadWrite.All"
        "c5366453-9fb0-48a5-a156-24f0c49a4b84" = "Directory.ReadWrite.All"
        "7e05723c-0bb0-42da-be95-ae9f08a6e53c" = "Domain.ReadWrite.All"
        "0b5d694c-a244-4bde-86e6-eb5cd07730fe" = "Domain.ReadWrite.All"
        "d1808e82-ce13-47af-ae0d-f9b254e6d58a" = "EduRoster.ReadWrite.All"
        "62a82d76-70ea-41e2-9197-370581804d09" = "Group.ReadWrite.All"
        "4e46008b-f24c-477d-8fff-7bb4ec7aafe0" = "Group.ReadWrite.All"
        "658aa5d8-239f-45c4-aa12-864f4fc7e490" = "Member.Read.Hidden"
        "f6a3db3e-f7e8-4ed2-a414-557c8c9830be" = "Member.Read.Hidden"
        "483bed4a-2ad3-4361-a73b-c83ccdbdc53c" = "RoleManagement.ReadWrite.Directory"
        "9e3f62cf-ca93-4989-b6ce-bf83c28f9fe8" = "RoleManagement.ReadWrite.Directory"
        "741f803b-c850-494e-b5df-cde7c675a1ca" = "User.ReadWrite.All"
        "204e0828-b5ca-4ad8-b9f3-f32a958e7cc4" = "User.ReadWrite.All"
        "637d7bec-b31e-4deb-acc9-24275642a2c9" = "User.ManageIdentities.All"
        "c529cfca-c91b-489c-af2b-d92990b66ce6" = "User.ManageIdentities.All"
        "633e0fce-8c58-4cfb-9495-12bbd5a24f7c" = "Policy.Read.ConditionalAccess"
        "01c0a623-fc9b-48e9-b794-0756f8e8f067" = "Policy.ReadWrite.ConditionalAccess"
        "f1493658-876a-4c87-8fa7-edb559b3476a" = "DeviceManagementConfiguration.Read.All"
        "0883f392-0a7a-443d-8c76-16a6d39c7b63" = "DeviceManagementConfiguration.ReadWrite.All"
        "0c5e8a55-87a6-4556-93ab-adc52c4d862d" = "DeviceManagementRBAC.ReadWrite.All"
        "662ed50a-ac44-4eef-ad86-62eed9be2a29" = "DeviceManagementServiceConfig.ReadWrite.All"
        "7e823077-d88e-468f-a337-e18f1f0e6c7c" = "Policy.ReadWrite.AuthenticationMethod"
        "2672f8bb-fd5e-42e0-85e1-ec764dd2614e" = "Policy.ReadWrite.PermissionGrant"
        "3c3c74f5-cdaa-4a97-b7e0-4e788bfcfb37" = "PrivilegedAccess.ReadWrite.AzureAD"
        "a84a9652-ffd3-496e-a991-22ba5529156a" = "PrivilegedAccess.ReadWrite.AzureResources"
        "8c026be3-8e26-4774-9372-8d5d6f21daff" = "RoleAssignmentSchedule.ReadWrite.Directory"
        "39d65650-9d3e-4223-80db-a335590d027e" = "TeamSettings.ReadWrite.All"
        "63dd7cd9-b489-4adf-a28c-ac38b9a0f962" = "User.Invite.All"
        "c67b52c5-7c69-48b6-9d48-7b3af3ded914" = "APIConnectors.ReadWrite.All"
    }
    # role is application which is background
    # Scope is delegated
    # has table is permissionname / where (e.g. Directory.Read.All/role)
    write-output "Getting Audit log..."
    $startDate = (get-date).adddays(-30).tostring("yyyy-MM-dd")
    $updatedApps= Get-MgAuditLogDirectoryAudit -Filter "ActivityDisplayName eq 'Update application' and activityDateTime ge $startDate"
    write-output "Getting Apps..."
    $allApps = Get-MgApplication
    $riskyAppList = [System.Collections.Generic.List[AzureAdApp]]::new()
    write-output "Scanning API permissions on $($allApps.count) apps"
    foreach ($azAdApp in $allApps) {
        $riskyApp = [AzureAdApp]::new($azAdApp.DisplayName, $azAdApp.appId, $false)
        $appRequiredResourceAccess = ($azAdApp.requiredresourceaccess.resourceaccess | convertto-json) | convertfrom-json
        $appRequiredResourceAccess | % { 
            $permName = ""
            try {
                $permName = $riskyPermissions[$($_.id)]
                if (!([string]::IsNullOrEmpty($permName))) {
                    if ($_.type -eq "Role" ) {
                        $permType = "Application (Role)"
                    } else {
                        $permType = "Delegated (Scope)"
                    }
                    $riskyApp.riskyPermissions.Add($permName, $permType)
                    $riskyApp.isRisky = $true
                }  
            } catch {}
            
        }
        if ($riskyApp.isRisky) {
            try {
                if (!([string]::IsNullOrEmpty($azAdApp.CreatedDateTime))) {
                    $riskyApp.createdDateTime = $azAdApp.CreatedDateTime.tostring()
                }
            } catch {

            }
            #try to get the last modified info
            try {
                $appModifiedRecords = (($updatedApps | ? { $_.AdditionalDetails.value -eq $azAdApp.AppId }) | sort -Property ActivityDateTime)
                if ($appModifiedRecords) {
                    if (($appModifiedRecords).count -gt 1) {
                        #it's an array
                        $riskyApp.lastModifiedBy = $appModifiedRecords[0].initiatedby.user.userprincipalname.trim()
                        $riskyApp.modifiedDateTime = $appModifiedRecords[0].activitydatetime.tostring()
                    } else {
                        $riskyApp.lastModifiedBy = $appModifiedRecords.initiatedby.user.userprincipalname.trim()
                        $riskyApp.modifiedDateTime = $appModifiedRecords.activitydatetime.tostring()
                    }
                }
            } catch {}
            $riskyAppList.add($riskyApp)
        }
    }
    if ($riskyAppList.count -gt 0) {
        write-host "Potentially risky Apps's" -ForegroundColor cyan
        $riskyAppList | ft -AutoSize
        # dump riskygpolist to json somewhere for further review
        $riskyAppList | convertto-json -depth 9 | out-file $env:temp\riskyAppList.json -force
        write-host "======Report file located at $env:temp\riskyAppList.json======" -ForegroundColor darkmagenta -BackgroundColor white
    } else {
        write-host "No potentially risky App's found"
    }
}
function Get-ClawGpoGroups 
{
    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )
    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Entering function Get-ClawGpoGroups" -logSev "Info" | out-null
    write-verbose "Entering function Get-ClawGpoGroups"
    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
    # create the return object and populate with objects we'll always need
    $clawGpoGroups = @{
        #this SID represents a collection of all child domains domain admin groups
        allChildDomainAdmins = $null
        #this SID represents a collection of all child domains tier 0 operators groups
        allChildTier0Operators = $null
        #this SID represents local performance log users
        builtinPerformanceLogUsers = "*S-1-5-32-559"
        #this SID represents the claw domain join group in the current domain
        clawDomainJoin = $null
        #this SID represents domain account operators
        domainAcctOps = "*S-1-5-32-548"
        #this SID represents the domain admins group in the current domain
        domainAdmins = "*$($myDomain.domainsid)-512"
        #this SID represents the RID 500 account in the current domain
        domainAdministratorAccount = "*$($myDomain.domainsid)-500"
        #this SID represents domain authenticated users
        domainAuthUsers = "*S-1-5-11"
        #this SID represents domain backup operators
        domainBackupOps = "*S-1-5-32-551"
        #this SID represents domain cryptographic operators
        domainCrypto = "*S-1-5-32-569"
        #this SID represents the domain domain controllers group in the current domain
        domainDCs = "*$($myDomain.domainsid)-516"
        #this SID represents domain enterprise domain controllers
        domainEntDcs = "*S-1-5-9"
        #this SID represents the domain group policy creator owners group in the current domain
        domainGPCO = "*$($myDomain.domainsid)-520"
        #this SID represents the domain domain guests group in the current domain
        domainGuests = "*$($myDomain.domainsid)-514"
        #this SID represents the domain guest account in the current domain
        domainGuestAccount = "*$($myDomain.domainsid)-501"
        #this SID represents domain print operators
        domainPrintOps = "*S-1-5-32-550"
        #this SID represents the domain read-only domain controllers group in the current domain
        domainRODCs = "*$($myDomain.domainsid)-521"
        #this SID represents domain server operators
        domainServerOps = "*S-1-5-32-549"
        #this SID represents the domain domain users group in the current domain
        domainUsers = "*$($myDomain.domainsid)-513"
        #this SID represents the forest enterprise admins group in the current forest
        enterpriseAdmins = "*$($myDomain.forestSid)-519"
        #this SID represents the enterprise read-only domain controllers group in the current forest
        enterpriseReadOnlyDomainControllers = $null
        #this SID represents the ESX Admins group needed for the CLAWv3 restricted groups GPO
        esxAdmins = $null
        #this SID represents the domain exchange servers group in the current domain
        exchangeServers = $null
        #this SID represents local account and member of administrators group
        localAccountAndAdmins = "*S-1-5-114"
        #this SID represents the local and domain administrators group
        localAdministrators = "*S-1-5-32-544"
        #this SID represents the domain admins group in the current domain
        localDomainAdmins = "*$($myDomain.domainsid)-512"
        #this SID represents the BUILTIN\Users group on local machines
        localUsers = "*S-1-5-32-545"
        #this SID represents NT AUTHORITY\All Services
        ntAllServices = "*S-1-5-80-0"
        #this SID represents NT AUTHORITY\SYSTEM
        ntAuthSystem = "*S-1-5-18"
        #this SID represents NT AUTHORITY\Local Service
        ntLocalService = "*S-1-5-19"
        #this SID represents NT AUTHORITY\Network Service
        ntNetService = "*S-1-5-20"
        #this SID represents generic services
        ntService = "*S-1-5-6"
        #this SID represents the read-only domain controllers group in the current domain
        readOnlyDomainControllers = $null
        #this SID represents the domain admins group in the current forest
        #if we have multi domains we have a root DA but this will be the same as the existing domain DA if we're running this in root
        rootDomainAdmins = "*$($myDomain.forestSid)-512"
        #this SID represents the tier 0 operators group in the forest root (will be empty if run in root because we capture this in the existing tier 0 operators)
        rootTier0Operators = $null
        #this SID represents the tier 0 service accounts group in the forest root (will be empty if run in root because we capture this in the existing tier 0 service accounts)
        rootTier0ServiceAccounts = $null
        #this SID represents the tier 1 operators group in the forest root (will be empty if run in root because we capture this in the existing tier 1 operators)
        rootTier1Operators = $null
        #this SID represents the tier 2 operators group in the forest root (will be empty if run in root because we capture this in the existing tier 2 operators)
        rootTier2Operators = $null
        #this SID represents the forest schema admins group in the current forest
        schemaAdmins = "*$($myDomain.forestSid)-518"
        #is this a single forest/single domain?
        singleForestSingleDomain = $false
        #this SID represents the tier 0 operators group in the current domain
        tier0Operators = $null
        #this SID represents the tier 0 service accounts group in the current domain
        tier0ServiceAccounts = $null
        #this SID represents the tier 1 operators group in the current domain
        tier1Operators = $null
        #this SID represents the tier 2 operators group in the current domain
        tier2Operators = $null

        
    }
    try {
        $clawGpoGroups["tier0Operators"] = '*{0}' -f $mydomain.GetStringSidFromBytes([byte[]]($mydomain.GetObjectByName("Tier 0 Operators","domain",$false).properties["objectsid"])[0])
    } catch {}
    try {
        $clawGpoGroups["tier0ServiceAccounts"] = '*{0}' -f $mydomain.GetStringSidFromBytes([byte[]]($mydomain.GetObjectByName("Tier 0 Service Accounts","domain",$false).properties["objectsid"])[0])
    } catch {}
    try {
        $clawGpoGroups["tier1Operators"] = '*{0}' -f $mydomain.GetStringSidFromBytes([byte[]]($mydomain.GetObjectByName("Tier 1 Operators","domain",$false).properties["objectsid"])[0])
    } catch {}
    try {
        $clawGpoGroups["tier2Operators"] = '*{0}' -f $mydomain.GetStringSidFromBytes([byte[]]($mydomain.GetObjectByName("Tier 2 Operators","domain",$false).properties["objectsid"])[0])
    } catch {}
    try {
        $clawGpoGroups["clawDomainJoin"] = '*{0}' -f $mydomain.GetStringSidFromBytes([byte[]]($mydomain.GetObjectByName("CLAW Domain Join","domain",$false).properties["objectsid"])[0])
    } catch {}
    try {
        $clawGpoGroups["readOnlyDomainControllers"] = '*{0}' -f $mydomain.GetStringSidFromBytes([byte[]]($mydomain.GetObjectByName("Read-only Domain Controllers","domain",$false).properties["objectsid"])[0])
    } catch {}
    try {
        $clawGpoGroups["enterpriseReadOnlyDomainControllers"] = '*{0}' -f $mydomain.GetStringSidFromBytes([byte[]]($mydomain.GetObjectByName("Enterprise Read-only Domain Controllers","forest",$false).properties["objectsid"])[0])
    } catch {}
    try {
        $clawGpoGroups["exchangeServers"] = '*{0}' -f $mydomain.GetStringSidFromBytes([byte[]]($mydomain.GetObjectByName("Exchange Servers","domain",$false).properties["objectsid"])[0])
    } catch {
        try {
            $clawGpoGroups["exchangeServers"] = '*{0}' -f $mydomain.GetStringSidFromBytes([byte[]]($mydomain.GetObjectByName("Exchange Servers","forest",$false).properties["objectsid"])[0])
        } catch {}
    }
    try {
        $clawGpoGroups["esxAdmins"] = '*{0}' -f $mydomain.GetStringSidFromBytes([byte[]]($mydomain.GetObjectByName("ESX Admins","domain",$false).properties["objectsid"])[0])
    } catch {}
    # now spider discovery to get other root/child domains unique items
    # how many domains we got in this forest anyway
    if ($myDomain.adForest.domains.count -gt 1) {
        # we have many
        $clawGpoGroups["singleForestSingleDomain"]=$false
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Multi domains detected: $($myDomain.adForest.domains.count)" -logSev "Info" | out-null
        # new logic for discovery: we create the collection of domains to search based on where we are. the logic to discover them is the same
        # if in root, simply get all child. if in child, get "other" domains
        # where are we
        if ($myDomain.domainControllerDetail.domain -eq $myDomain.domainControllerDetail.forest) {
            # we're in root
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Running in root: $($myDomain.domainControllerDetail.forest)" -logSev "Info" | out-null
            # get list of child domains
            $domainsToDiscover = $myDomain.domainDetail.ChildDomains 
        } else {
            # we're in child
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Running in child: $($myDomain.domainFqdn)" -logSev "Info" | out-null
            
            # grab the other root groups 
            try {
                $clawGpoGroups["rootTier0Operators"] = '*{0}' -f $mydomain.GetStringSidFromBytes([byte[]]($mydomain.GetObjectByName("Tier 0 Operators","forest",$false).properties["objectsid"])[0])
            } catch {}
            try {
                $clawGpoGroups["rootTier0ServiceAccounts"] = '*{0}' -f $mydomain.GetStringSidFromBytes([byte[]]($mydomain.GetObjectByName("Tier 0 Service Accounts","forest",$false).properties["objectsid"])[0])
            } catch {}
            try {
                $clawGpoGroups["rootTier1Operators"] = '*{0}' -f $mydomain.GetStringSidFromBytes([byte[]]($mydomain.GetObjectByName("Tier 1 Operators","forest",$false).properties["objectsid"])[0])
            } catch {}
            try {
                $clawGpoGroups["rootTier2Operators"] = '*{0}' -f $mydomain.GetStringSidFromBytes([byte[]]($mydomain.GetObjectByName("Tier 2 Operators","forest",$false).properties["objectsid"])[0])
            } catch {}
            
            # where name not like our domain or not like the forest root (since we already discovered forest)
            $domainsToDiscover = $myDomain.adForest.Domains | ? { !($_ -like $myDomain.domainFqdn) -and !($_ -like $myDomain.forestFqdn)}
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Horizontal discovery in child found other domains: $($otherChildDomains -join ',')" -logSev "Info" | out-null
        }
        
    } else {
        $clawGpoGroups["singleForestSingleDomain"]=$true
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Single forest, single domain detected." -logSev "Info" | out-null
    }
    return $clawGpoGroups
}
function Get-ClawOu
{

    ##########################################################################################################
    <#
    .SYNOPSIS
        Checks for CLAW OU structure.
    
    .DESCRIPTION
        Checks CLAW OU structure

    .EXAMPLE
        Get-ClawOu

        Checks for the required OU's for CLAW

    .OUTPUTS
        Verbose output to host.
        General log written to '$env:temp\claw.log'

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

    ###################################
    ## Function Options and Parameters
    ###################################

    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$false)]
        [string]$IDOUName="SITH",
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )
    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Entering function Get-ClawOu" -logSev "Info" | out-null
    write-verbose "Entering function Get-ClawOu"
    #Get domain object
    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
    # where is SITH
    $IDOUNamePath = Get-IDOUNamePath -IDOUName $IDOUName -myDomain $mydomain
    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Get-ClawOu check locations: DomainDn: $($myDomain.DomainDn). IDOUNamePath: $IDOUNamePath" -logSev "Info" | out-null
    $returnVal = $true
    # build OU tree. start with what always gets created
    $clawOus = Get-ClawOuManifest -IDOUNamePath $IDOUNamePath -myDomain $myDomain
    
    # create ou's
    foreach ($ou in $clawOus) {
        # idempotency test
        $ouCheck=""
        try {
            $ouCheck = $myDomain.GetObjectByDn($ou,"domain")
        } catch {}
        if (!($ouCheck)) {
            $returnVal = $false
            return $returnVal
        }
    }
    return $returnVal
}
function Get-ClawOuManifest
{
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$true)]
        [string]$IDOUNamePath,
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )

    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Entering function Get-ClawOuManifest" -logSev "Info" | out-null
    write-verbose "Entering function Get-ClawOuManifest"
    #Get domain object
    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain

    $clawOus = @()
    $clawOus += "OU=CLAW,$($myDomain.DomainDn)"
    $clawOus += "OU=Computer Quarantine,OU=CLAW,$($myDomain.DomainDn)"
    $clawOus += "$IDOUNamePath"
    $clawOus += "OU=Staging,$IDOUNamePath"
    $clawOus += "OU=Tier 0,$IDOUNamePath"
    $clawOus += "OU=T0-Accounts,OU=Tier 0,$IDOUNamePath"
    $clawOus += "OU=T0-Groups,OU=Tier 0,$IDOUNamePath"
    $clawOus += "OU=T0-Service Accounts,OU=Tier 0,$IDOUNamePath"
    $clawOus += "OU=T0-Admin Workstations,OU=Tier 0,$IDOUNamePath"
    $clawOus += "OU=T0-Servers,OU=Tier 0,$IDOUNamePath"
    $clawOus += "OU=T0-Operators,OU=T0-Groups,OU=Tier 0,$IDOUNamePath"
    $clawOus += "OU=T0-Database,OU=T0-Servers,OU=Tier 0,$IDOUNamePath"
    $clawOus += "OU=T0-Identity,OU=T0-Servers,OU=Tier 0,$IDOUNamePath"
    $clawOus += "OU=T0-Management,OU=T0-Servers,OU=Tier 0,$IDOUNamePath"
    $clawOus += "OU=T0-PKI,OU=T0-Servers,OU=Tier 0,$IDOUNamePath"
    $clawOus += "OU=T0-Backup,OU=T0-Servers,OU=Tier 0,$IDOUNamePath"
    $clawOus += "OU=T0-Virtualization,OU=T0-Servers,OU=Tier 0,$IDOUNamePath"
    $clawOus += "OU=Groups,OU=CLAW,$($myDomain.DomainDn)"
    $clawOus += "OU=Tier 1 Servers,OU=CLAW,$($myDomain.DomainDn)"
    $clawOus += "OU=T2-Devices,OU=CLAW,$($myDomain.DomainDn)"
    $clawOus += "OU=User Accounts,OU=CLAW,$($myDomain.DomainDn)"
    $clawOus += "OU=Security Groups,OU=Groups,OU=CLAW,$($myDomain.DomainDn)"
    $clawOus += "OU=Distribution Groups,OU=Groups,OU=CLAW,$($myDomain.DomainDn)"
    $clawOus += "OU=Contacts,OU=Groups,OU=CLAW,$($myDomain.DomainDn)"
    $clawOus += "OU=Application,OU=Tier 1 Servers,OU=CLAW,$($myDomain.DomainDn)"
    $clawOus += "OU=Event Forwarding,OU=Tier 1 Servers,OU=CLAW,$($myDomain.DomainDn)"
    $clawOus += "OU=Collaboration,OU=Tier 1 Servers,OU=CLAW,$($myDomain.DomainDn)"
    $clawOus += "OU=Database,OU=Tier 1 Servers,OU=CLAW,$($myDomain.DomainDn)"
    $clawOus += "OU=Messaging,OU=Tier 1 Servers,OU=CLAW,$($myDomain.DomainDn)"
    $clawOus += "OU=Staging,OU=Tier 1 Servers,OU=CLAW,$($myDomain.DomainDn)"
    $clawOus += "OU=Desktops,OU=T2-Devices,OU=CLAW,$($myDomain.DomainDn)"
    $clawOus += "OU=Kiosks,OU=T2-Devices,OU=CLAW,$($myDomain.DomainDn)"
    $clawOus += "OU=Laptops,OU=T2-Devices,OU=CLAW,$($myDomain.DomainDn)"
    $clawOus += "OU=Staging,OU=T2-Devices,OU=CLAW,$($myDomain.DomainDn)"
    $clawOus += "OU=Enabled Users,OU=User Accounts,OU=CLAW,$($myDomain.DomainDn)"
    $clawOus += "OU=Disabled Users,OU=User Accounts,OU=CLAW,$($myDomain.DomainDn)"
    $clawOus += "OU=Tier 1,$IDOUNamePath"
    $clawOus += "OU=Tier 2,$IDOUNamePath"
    $clawOus += "OU=T1-Accounts,OU=Tier 1,$IDOUNamePath"
    $clawOus += "OU=T1-Groups,OU=Tier 1,$IDOUNamePath"
    $clawOus += "OU=T1-Admin Workstations,OU=Tier 1,$IDOUNamePath"
    $clawOus += "OU=T1-Operators,OU=T1-Groups,OU=Tier 1,$IDOUNamePath"
    $clawOus += "OU=T2-Accounts,OU=Tier 2,$IDOUNamePath"
    $clawOus += "OU=T2-Groups,OU=Tier 2,$IDOUNamePath"
    $clawOus += "OU=T2-Service Accounts,OU=Tier 2,$IDOUNamePath"
    $clawOus += "OU=T2-Admin Workstations,OU=Tier 2,$IDOUNamePath"
    $clawOus += "OU=T2-Operators,OU=T2-Groups,OU=Tier 2,$IDOUNamePath"
    return $clawOus
}
function Get-DomainsInForestAsAdalList {
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
        $credential = [System.Management.Automation.PSCredential]::Empty
    )
    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Entering Get-DomainsInForestAsAdalList" -logSev "Info" | out-null
    $domainList = [System.Collections.Generic.List[AdAl]]::new()
    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain -credential $credential
    # get list of domains in forest
    foreach ($dDomain in ($myDomain.adForest.Domains)) {
        write-verbose "Discovering reachable DC's in $dDomain"
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Discovering reachable DC's in $dDomain" -logSev "Info" | out-null
        # foreach domain get a connectable dc
        if ($Credential -ne [System.Management.Automation.PSCredential]::Empty) {
            $myConnnectedDomain = [Adal]::new($Credential)   
        } else {
            $myConnnectedDomain = [Adal]::new()
        }
        try {
            $myConnnectedDomain.AutoDomainDiscovery($dDomain)
            if ([string]::IsNullOrEmpty($myDomain.chosenDc)) {
                throw
            }
        } catch {
            write-error "Failed to discover domain $dDomain"
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to discover domain $dDomain" -logSev "Error" | out-null
        }
        
        if (-not [string]::IsNullOrEmpty($myDomain.chosenDc)) {
            ### add this to an arraylist
            write-verbose "Found reachable DC: $($myConnnectedDomain.chosenDc)"
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Found reachable DC: $($myConnnectedDomain.chosenDc)" -logSev "Info" | out-null
            $domainList.Add($myConnnectedDomain)   
        }
    }
    return $domainList
}
function Get-EntraObject
{
  [CmdletBinding()]
  param
  (
    [Parameter(Mandatory = $true)]
    #[ValidateSet([graphObjectTypeValidator])] # This is not working in powershell 5.0
    [ValidateScript({
      $validValues = [graphObjectTypePermissions]::new().getAll().Keys
      if ($_ -in $validValues) {
          $true
      } else {
          throw "Invalid value '$_'. Valid values are: $($validValues -join ', ')"
      }
    })]
    [string] $objectType,
  
    [Parameter(Mandatory = $false)]
    [string] $objectId,

    [Parameter(Mandatory = $false)]
    [string] $filter,

    [Parameter(Mandatory = $false)]
    [string] $objectProperties,

    [Parameter(mandatory = $false)]
    [string]
    $apiVersion
  )

  $graphCollection = [GraphAL]::new(@{objectType = $objectType})

  # Fill the parameters for the query
  $params = @{}
  
  if (![string]::IsNullOrEmpty($objectId)) # query for a specific object
  {
    $params.Add("objectId",$objectId)
  } 
  elseif (![string]::IsNullOrEmpty($filter)) # query using a filter
  {
    $params.Add("filter",$filter) # Example: $filter = "startswith(displayName,'Mircat')"
  }

  if (![string]::IsNullOrEmpty($objectProperties)) # Return specific object attributes
  {
    $params.Add("objectProperties", $objectProperties) # Example: $objectProperties = "id,displayName"
  }

  if (![string]::IsNullOrEmpty($apiVersion)) # Use a specific API version
  {
    $params.Add("apiVersion", $apiVersion)
  }
  
  # Execute the query
  if (![string]::IsNullOrEmpty($objectId))
  {
    $graphCollection.Get($params)
  } Else { 
    $graphCollection.Get($params).value
  }
}
function Get-EntraObjectOwner
{
  [CmdletBinding()]
  param
  (
    [Parameter(Mandatory = $true)]
    #[ValidateSet([graphObjectTypeValidator])] # This is not working in powershell 5.0
    [ValidateScript({
      $validValues = [graphObjectTypePermissions]::new().getAll().Keys
      if ($_ -in $validValues) {
          $true
      } else {
          throw "Invalid value '$_'. Valid values are: $($validValues -join ', ')"
      }
    })]
    [string] $objectType,
  
    [Parameter(Mandatory = $true)]
    [string] $objectId
  )

    $graphCollection = [GraphAL]::new(@{objectType = $objectType})
    Try
    {
      $graphCollection.getOwner($objectId).value
    } Catch {
      Write-Error $_.Exception.Message
    }

}
function Get-GpoWMIFilter {
    [CmdletBinding()]
    Param(
        #The gpo guid
        [parameter(Mandatory=$True,Position=1)]
        [String]$wmiFilterName,
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
        )
    
    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
    $returnVal = $null
    $SearchRoot = [adsi]("LDAP://CN=SOM,CN=WMIPolicy,CN=System,"+$($myDomain.domainDn))
    $search = new-object System.DirectoryServices.DirectorySearcher($SearchRoot)
    $search.filter = "(&(objectclass=msWMI-Som)(msWMI-Name=$wmiFilterName))"
    try {
        $results = $search.FindOne()
    } catch {}
    $search.dispose()
    if ($results) {
        $returnVal = [PSCustomObject]@{ 
            name=$results.properties["mswmi-name"].item(0)
            id=$results.properties["mswmi-id"].item(0)
        }
    }
    return $returnVal
}
function Get-IDOUNamePath {
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$false)]
        [string]$IDOUName="SITH",
        [Parameter(DontShow)]
        [AllowEmptyString()]
        [string]$domain,
        [Parameter(DontShow)]
        [AllowEmptyString()]
        $myDomain
    )
    #Get domain object
    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
    # where is SITH
    $IDOUNamePath = $(($myDomain.GetObjectByFilter("(adminDescription=SITH-MIRCAT)","domain").properties).distinguishedname)
    if (!($IDOUNamePath)) {
        #this means we couldn't find it so set the default
        $IDOUNamePath = "OU=$IDOUName,OU=CLAW,$($myDomain.domainDn)"
    }
    try {
        # does SITH tag match provided OU
        $providedIDOUPath = $(($myDomain.GetObjectByName($IDOUName,"domain",$true).properties).distinguishedname)
        if (-not ([string]::IsNullOrEmpty($providedIDOUPath))) {
            if ([string]::compare($IDOUNamePath,$providedIDOUPath,$true) -ne 0) {
                write-warning "Provided IDOUName of $IDOUName does not match OU tagged by CLAW module. Using OU at $IDOUNamePath instead of user provided input."
            }
        }
    } catch {}
    return $IDOUNamePath
}
function Get-MDIAccessKey {
    # https://github.com/martin77s/M365D/blob/main/mdiDeploymentPackage.ps1
    param(
        [Parameter(Mandatory = $true)] [string] $accessToken
    )
    $uri = "https://graph.microsoft.com/beta/security/identities/sensors/getDeploymentAccessKey"
    $headers = @{
        'Authorization' = 'Bearer ' + $accessToken
    }
    $accessKey = (Invoke-WebRequest -Uri $uri -UseBasicParsing -Headers  $headers -Method Get).Content
    $accessKey
}
function Get-MDIAccessToken {
    # thanks to Nico van Diemen
    $returnVal = $null
    
    try {
        $regIe = get-itemproperty 'HKLM:\SOFTWARE\Microsoft\Active Setup\Installed Components\{A509B1A7-37EF-4b3f-8CFC-4F3A74704073}'
        if ($regIe.IsInstalled -eq 1) {
            set-itemproperty 'HKLM:\SOFTWARE\Microsoft\Active Setup\Installed Components\{A509B1A7-37EF-4b3f-8CFC-4F3A74704073}' -name "IsInstalled" -value 0 -type DWord
        }

        $clientId = "14d82eec-204b-4c2f-b7e8-296a70dab67e"
        $tenantId = "common"
        $scope = "https://graph.microsoft.com/SecurityIdentitiesSensors.Read.All"
        $deviceCodeEndpoint = "https://login.microsoftonline.com/$tenantId/oauth2/v2.0/devicecode"
        $tokenEndpoint = "https://login.microsoftonline.com/$tenantId/oauth2/v2.0/token"

        # Request device code
        $deviceCodeRequestBody = @{
            client_id = $clientId
            scope     = $scope
        }
        $deviceCodeResponse = Invoke-RestMethod -Method Post -Uri $deviceCodeEndpoint -ContentType "application/x-www-form-urlencoded" -Body $deviceCodeRequestBody
        
        # Display the device code and URL
        Write-Host "To sign in, use a web browser to open the page" $deviceCodeResponse.verification_uri "and enter the code" $deviceCodeResponse.user_code
        
        # Wait for the user to authenticate
        Start-Sleep -Seconds 30
        
        # Request access token using the device code
        $tokenRequestBody = @{
            client_id = $clientId
            scope     = $scope
            grant_type = "urn:ietf:params:oauth:grant-type:device_code"
            device_code = $deviceCodeResponse.device_code
        }
        
        # Poll for token
        $tokenResponse = $null
        do {
            try {
                $tokenResponse = Invoke-RestMethod -Method Post -Uri $tokenEndpoint -ContentType "application/x-www-form-urlencoded" -Body $tokenRequestBody -ErrorAction SilentlyContinue
            } catch {
                start-sleep -seconds 1
            }
            
            if (-not $tokenResponse) { Start-Sleep -Seconds 5 }
        } while (-not $tokenResponse)
        
        # Extract and display the access token
        $mdiToken = $tokenResponse.access_token
        $returnVal = $mdiToken
    } catch {
        write-error "Failed to generate MDI access token. $($_.Exception)"
    } finally {
        if ($regIe) {
            set-itemproperty 'HKLM:\SOFTWARE\Microsoft\Active Setup\Installed Components\{A509B1A7-37EF-4b3f-8CFC-4F3A74704073}' -name "IsInstalled" -value $regIe.IsInstalled -type DWord
        }
    }

    return $returnVal

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
function Get-MdiCaptureComponent
{
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param
    (
        [Parameter()]
        [AllowNull()]
        [string] $ComputerName
    )

    $uninstallRegKey = 'SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall'
    $return = @()
    try
    {
        foreach ($registryView in @('Registry32', 'Registry64'))
        {
            if ($ComputerName)
            {
                $hklm = [Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey('LocalMachine', $ComputerName, $registryView)
            }
            else
            {
                $hklm = [Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine', $registryView)
            }

            $uninstallRef = $hklm.OpenSubKey($uninstallRegKey)
            $applications = $uninstallRef.GetSubKeyNames()

            foreach ($app in $applications)
            {
                $appDetails = $hklm.OpenSubKey($uninstallRegKey + '\' + $app)
                $appDisplayName = $appDetails.GetValue('DisplayName')
                $appVersion = $appDetails.GetValue('DisplayVersion')
                if ($appDisplayName -match 'npcap|winpcap')
                {
                    $return += '{0} ({1})' -f $appDisplayName, $appVersion
                }
            }
            $hklm.Close()
        }
    }
    catch
    {
        $return = 'N/A'
    }

    ($return -join ', ')
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
function Get-MDIDomainReadiness {


    [CmdletBinding()]
    Param(
        [Parameter(Mandatory = $true)]
        [string]$Identity,
        [parameter(Mandatory = $true)]
        [ValidateSet('Domain', 'Forest')]
        [string]$IdentityLocation,
        [Parameter(Mandatory = $false)]
        [switch]$ForceStandardAccount,
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )

    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
    if (($myDomain.forestSid -ne $myDomain.domainSid) -and ($IdentityLocation -eq 'Forest')) {
        $myDomain = Initialize-MyDomain -domain $myDomain.forestFqdn
    }

    [MdiDomainReadiness]$myMdiDomainReadiness = [MdiDomainReadiness]::new($myDomain)
    #validate service account
    $serviceAccount = $mydomain.GetObjectByFilter("|(samaccountname=$identity)(samaccountname=$identity$)",$IdentityLocation)
    
    if ([string]::IsNullOrEmpty($serviceAccount)) {
        $myMdiDomainReadiness.identityExists = $false
        $myMdiDomainReadiness.isGmsa = (!($forceStandardAccount))
        if ($myMdiDomainReadiness.isGmsa) {
            $myMdiDomainReadiness.identity = $identity
        }
    } else {
        $myMdiDomainReadiness.identityExists = $true
        $myMdiDomainReadiness.identity = $serviceAccount.Properties["samaccountname"]
        $myMdiDomainReadiness.identitySid = $($myDomain.GetStringSidFromBytes([byte[]]$($serviceAccount.properties.objectsid)))
        if ("msDS-GroupManagedServiceAccount" -in $serviceAccount.properties.objectclass) {
            $myMdiDomainReadiness.isGmsa = $true
        } else {
            $myMdiDomainReadiness.isGmsa = $false
        }
    }
    
    $myMdiDomainReadiness.identityLocation = $IdentityLocation
    $myMdiDomainReadiness.GetOptionalComponents()
    $myMdiDomainReadiness.AdvancedAuditPolicyCAs.GpoExists = [bool]($myDomain.GetGpo("$($myMdiDomainReadiness.AdvancedAuditPolicyCAs.GpoName)",$null))
    $myMdiDomainReadiness.AdvancedAuditPolicyDCs.GpoExists = [bool]($myDomain.GetGpo("$($myMdiDomainReadiness.AdvancedAuditPolicyDCs.GpoName)",$null))
    $myMdiDomainReadiness.EntraIDAuditing.GpoExists = [bool]($myDomain.GetGpo("$($myMdiDomainReadiness.EntraIDAuditing.GpoName)",$null))
    $myMdiDomainReadiness.LogonAsService.GpoExists = [bool]($myDomain.GetGpo("$($myMdiDomainReadiness.LogonAsService.GpoName)",$null))
    $myMdiDomainReadiness.NTLMAuditing.GpoExists = [bool]($myDomain.GetGpo("$($myMdiDomainReadiness.NTLMAuditing.GpoName)",$null))
    $myMdiDomainReadiness.PerformanceLib.GpoExists = [bool]($myDomain.GetGpo("$($myMdiDomainReadiness.PerformanceLib.GpoName)",$null))
    $myMdiDomainReadiness.ProcessorPerformance.GpoExists = [bool]($myDomain.GetGpo("$($myMdiDomainReadiness.ProcessorPerformance.GpoName)",$null))
    $myMdiDomainReadiness.RemoteSAM.GpoExists = [bool]($myDomain.GetGpo("$($myMdiDomainReadiness.RemoteSAM.GpoName)",$null))
    $myMdiDomainReadiness.wmiFilterExists = [bool]($myDomain.GetWmiFilter("Tier 0 - No DC Apply",$IdentityLocation))
    $myMdiDomainReadiness.CreateGpoReport()
    Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($myMdiDomainReadiness.ToString())" -logSev "Info" | out-null
    return $myMdiDomainReadiness

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
function Get-MDISensorPackage {
    # https://github.com/martin77s/M365D/blob/main/mdiDeploymentPackage.ps1
    param(
        [Parameter(Mandatory = $true)] [string] $accessToken,
        [Parameter(Mandatory = $true)] [string] $workspaceName,
        [Parameter(Mandatory = $true)] [string] $path,
        [switch] $Force
    )

    if (-not (Test-Path $path)) {
        New-Item -ItemType Directory -Path $path | Out-Null
    }

    $latestLocalVersion = Get-ChildItem -Path $path -Directory | Where-Object { $_.Name -as [version] } |
        Sort-Object -Property { [version] $_.Name } -Descending | Select-Object -First 1 -ExpandProperty Name

    $uri = 'https://{0}.atp.azure.com/api/sensors/deploymentPackageUri' -f $workspaceName
    $headers = @{
        'Authorization' = 'Bearer ' + $accessToken
    }
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $downloadUri = (Invoke-WebRequest -Uri $uri -UseBasicParsing -Headers  $headers -Method Get).Content
    $cloudVersion = [version]($downloadUri -split '/')[5]

    if ($Force -or $cloudVersion -gt $latestLocalVersion) {
        $targetPath = (New-Item -Path $path -Name $cloudVersion.ToString() -ItemType Directory -Force)
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12;
        Invoke-WebRequest -Uri $downloadUri -Method Get -OutFile ('{0}\Azure ATP Sensor Setup.zip' -f $targetPath.FullName)
        $returnPath = $targetPath.FullName
    } else {
        $returnPath = Join-Path -Path (Get-Item -Path $path).FullName -ChildPath $latestLocalVersion
    }
    return ('{0}\Azure ATP Sensor Setup.zip' -f $targetPath.FullName)
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
Function Get-PIMRoleAssignment 
{
    <#
    .SYNOPSIS
        This will check if a user is added to PIM or standing access.
        For updated help and examples refer to -Online version.
    
    .NOTES
        Name: Get-PIMRoleAssignment
        Author: theSysadminChannel
        Version: 1.0
        DateCreated: 2021-May-15
    
    .EXAMPLE
        Get-PIMRoleAssignment -UserPrincipalName blightyear@thesysadminchannel.com
    
    .EXAMPLE
        Get-PIMRoleAssignment -RoleName 'Global Administrator'
    
    .LINK
        https://thesysadminchannel.com/get-pim-role-assignment-status-for-azure-ad-using-powershell -
    #>
 
    [CmdletBinding()]
    param(
        [Parameter(
            Mandatory = $false,
            ValueFromPipeline = $true,
            ValueFromPipelineByPropertyName = $true,
            ParameterSetName = 'User',
            Position  = 0
        )]
        [string[]]  $UserPrincipalName,
 
 
        [Parameter(
            Mandatory = $false,
            ValueFromPipeline = $true,
            ValueFromPipelineByPropertyName = $true,
            ParameterSetName = 'Role',
            Position  = 1
        )]
        [Alias('DisplayName')]
        [string]    $RoleName,
        [Parameter(Mandatory=$true)]
        [string]    $tenantId
    )
 
    BEGIN {
        Connect-Az -tenantId $tenantId | out-null
        $SessionInfo = Get-AzureADCurrentSessionInfo -ErrorAction Stop
        if (-not ($PSBoundParameters.ContainsKey('TenantId'))) {
            $TenantId = $SessionInfo.TenantId
        }
 
        $AdminRoles = Get-AzureADMSPrivilegedRoleDefinition -ProviderId aadRoles -ResourceId $TenantId -ErrorAction Stop | select Id, DisplayName
        $RoleId = @{}
        $AdminRoles | ForEach-Object {$RoleId.Add($_.DisplayName, $_.Id)}
    }
 
    PROCESS {
        if ($PSBoundParameters.ContainsKey('UserPrincipalName')) {
            foreach ($User in $UserPrincipalName) {
                try {
                    $AzureUser = Get-AzureADUser -ObjectId $User -ErrorAction Stop | select DisplayName, UserPrincipalName, ObjectId
                    $UserRoles = Get-AzureADMSPrivilegedRoleAssignment -ProviderId aadRoles -ResourceId $TenantId -Filter "subjectId eq '$($AzureUser.ObjectId)'"
 
                    if ($UserRoles) {
                        foreach ($Role in $UserRoles) {
                            $RoleObject = $AdminRoles | Where-Object {$Role.RoleDefinitionId -eq $_.id}
 
                            [PSCustomObject]@{
                                ObjectId          = $AzureUser.ObjectId
                                UserPrincipalName = $AzureUser.UserPrincipalName
                                AzureADRole       = $RoleObject.DisplayName
                                AzureADRoleId     = $RoleObject.id
                                PIMAssignment     = $Role.AssignmentState
                                MemberType        = $Role.MemberType
                            }
                        }
                    }
                } catch {
                    Write-Error $_.Exception.Message
                }
            }
        }
 
        if ($PSBoundParameters.ContainsKey('RoleName')) {
            try {
                $RoleMembers = @()
                $RoleMembers += Get-AzureADMSPrivilegedRoleAssignment -ProviderId aadRoles -ResourceId $TenantId -Filter "RoleDefinitionId eq '$($RoleId[$RoleName])'" -ErrorAction Stop | select RoleDefinitionId, SubjectId, StartDateTime, EndDateTime, AssignmentState, MemberType
 
                if ($RoleMembers) {
                    $RoleMemberList = $RoleMembers.SubjectId | select -Unique
                    $AzureUserList = foreach ($Member in $RoleMemberList) {
                        try {
                            Get-AzureADUser -ObjectId $Member | select ObjectId, UserPrincipalName
                        } catch {
                            try {
                                Get-AzureADGroup -ObjectId $Member -ErrorAction Stop | select ObjectId, @{Name = 'UserPrincipalName'; Expression = { "$($_.DisplayName) (Group)" }}
                                $GroupMemberList = Get-AzureADGroupMember -ObjectId $Member | select ObjectId, UserPrincipalName
                                foreach ($GroupMember in $GroupMemberList) {
                                    $RoleMembers += Get-AzureADMSPrivilegedRoleAssignment -ProviderId aadRoles -ResourceId $TenantId -Filter "RoleDefinitionId eq '$($RoleId[$RoleName])' and SubjectId eq '$($GroupMember.objectId)'" -ErrorAction Stop | select RoleDefinitionId, SubjectId, StartDateTime, EndDateTime, AssignmentState, MemberType
                                }
                                Write-Output $GroupMemberList
                            } catch {

                            }
                            
                        }
                    }
 
                    $AzureUserList = $AzureUserList | select ObjectId, UserPrincipalName -Unique
                    $AzureUserHash = @{}
                    $AzureUserList | ForEach-Object {$AzureUserHash.Add($_.ObjectId, $_.UserPrincipalName)}
 
                    foreach ($Role in $RoleMembers) {
                        [PSCustomObject]@{
                            ObjectId          = $(($AzureUserHash.GetEnumerator() | ? { $_.Value -eq $AzureUserHash[$Role.SubjectId] }).name)
                            UserPrincipalName = $AzureUserHash[$Role.SubjectId]
                            AzureADRole       = $RoleName
                            AzureADRoleId     = (get-mgdirectoryrole -Filter "DisplayName eq '$RoleName'").id
                            PIMAssignment     = $Role.AssignmentState
                            MemberType        = $Role.MemberType
                        }
                    }
                }
            } catch {
                Write-Error $_.Exception.Message
            }
        }
    }
 
    END {}
 
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
function Install-AdRsat {
    try {
        $pt = get-wmiobject -Query 'select producttype from win32_operatingsystem'
        if ($($pt.producttype) -ne 1) {
            #Get AD PowerShell feature
            $AdTools = Get-WindowsFeature -Name "RSAT-AD-Tools"

            #Check install status
            if ($AdTools.Installed -eq $false) {
                $Install = Add-WindowsFeature -Name "RSAT-AD-Tools"
            }
            #Get GPMC feature
            $GPMC = Get-WindowsFeature -Name "GPMC"

            #Check install status
            if ($GPMC.Installed -eq $false) {
                $Install = Add-WindowsFeature -Name "GPMC"
            }
        } else {
            $AdTools = Get-WindowsCapability -Name "RSAT*" -online | ? { $_.DisplayName -eq 'RSAT: Active Directory Domain Services and Lightweight Directory Services Tools' } 
            #
            if ($($ADTools.state) -ne 'Installed') {
                Get-WindowsCapability -Name "RSAT*" -online | ? { $_.DisplayName -eq 'RSAT: Active Directory Domain Services and Lightweight Directory Services Tools' } | Add-WindowsCapability -online
            }
            $Gpmc = Get-WindowsCapability -Name "*Group*" -online | ? { $_.DisplayName -eq 'RSAT: Group Policy Management Tools' } 
            if ($($Gpmc.state) -ne 'Installed') {
                Get-WindowsCapability -Name "*Group*" -online | ? { $_.DisplayName -eq 'RSAT: Group Policy Management Tools' } | Add-WindowsCapability -online
            }
        }
        
    } catch {
        write-warning "Unable to validate RSAT tools"
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Unable to validate RSAT tools $($_.Exception)" -logSev "Warn" | out-null
    }
}

function Install-ClawComponents 
{
     ##########################################################################################################
    <#
    .SYNOPSIS
        Checks for and installs components needed for CLAW management server.
    
    .DESCRIPTION
        Checks for and installs the following core components:

            * Active Direvtory PowerShell module
            * Group Policy PowerShell module
            * LAPS management components

        Install sequence logic ensures cmdlets are executed in correct order.

    .EXAMPLE
        Install-ClawComponents

    .OUTPUTS
        Verbose output to host.
        General log written to '$env:temp\claw.log'

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

    ###################################
    ## Function Options and Parameters
    ###################################

    #Requires -version 4
    #Requires -RunAsAdministrator

    #Define and validate parameters
    [CmdletBinding()]
    Param()

    write-output "Checking AD Web Services status"
    # if we're on a DC, check the service
    if (Test-IsDC) {
        $adws = get-service -name adws -erroraction silentlycontinue
        if (!($adws)) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "AD Web Services (ADWS) not found on system. Critical stop." -logSev "Error" | out-null
            throw "ADWS Service not found"
        } else {
            if ($adws.status -ne "Running") {
                write-Warning "AD Web Services (ADWS) installed but not running. StartType is $($adws.StartType)"
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "AD Web Services (ADWS) installed but not running. StartType is $($adws.StartType)" -logSev "Warn" | out-null
                write-verbose "Attempting to autofix ADWS"
                $setADWSstartType=set-service -name adws -StartupType automatic -erroraction silentlycontinue
                try {
                    $startADWS = start-service -name adws -erroraction silentlycontinue
                    start-sleep 5
                    $adwsCheck2 = get-service -name adws -erroraction silentlycontinue
                    if ($adwsCheck2.status -ne "Running") {
                        throw
                    } else {
                        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Fixed AD Web Services (ADWS) service. Installed and running." -logSev "Info" | out-null
                        write-verbose "Successfully autofixed ADWS"
                    }
                } catch {
                    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Unable to autofix ADWS. Please check system logs. Critical stop." -logSev "Error" | out-null
                    throw "Unable to autofix ADWS. Please check system logs."
                }
            } else {
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "AD Web Services (ADWS) installed and running. StartType is $($adws.StartType)" -logSev "Info" | out-null
            }
        }
    } else {
        #if we're not on a DC check the port
        
    }
    Install-AdRsat

}
function Invoke-AzureAdTenantTakeBack
{
    ##########################################################################################################
    <#
    .SYNOPSIS
        Takes an AAD tenant back from a threat actor by getting all privileged users, resetting their passwords, revoking their session
        and, optionally, removing them from their roles. You must specify a list of exempted users because you can't remove all privileged
        because you'll lock yourself out.
    
    .DESCRIPTION
        Takes an AAD tenant back from a threat actor by getting all privileged users, resetting their passwords, revoking their session
        and, optionally, removing them from their roles. You must specify a list of exempted users because you can't remove all privileged
        because you'll lock yourself out.

        Use the -whatif switch to test this

    .EXAMPLE
        Invoke-AzureAdTenantTakeBack -exemptedUsers "breakglass1@mytenant.onmicrosoft.com,breakglass2@mytenant.onmicrosoft.com" -tenantId "my-tenant-id"

        Specify the users that you are exempting from the takeback. THIS MUST BE ONE OF THE USERS THAT YOU'RE RUNNING THE CODE AS.
        Specify the tenant ID.
    .EXAMPLE
        Invoke-AzureAdTenantTakeBack -exemptedUsers "breakglass1@mytenant.onmicrosoft.com,breakglass2@mytenant.onmicrosoft.com" -tenantId "my-tenant-id" -removeFromRole

        Specify the users that you are exempting from the takeback. THIS MUST BE ONE OF THE USERS THAT YOU'RE RUNNING THE CODE AS.
        Specify the tenant ID.
        Specify that you want the affected users removed from their role additionally.
    .EXAMPLE
        Invoke-AzureAdTenantTakeBack -exemptedUsers "breakglass1@mytenant.onmicrosoft.com,breakglass2@mytenant.onmicrosoft.com" -tenantId "my-tenant-id" -removeFromRole -whatif

        Specify the users that you are exempting from the takeback using the -exemptedUsers argument. THIS MUST BE ONE OF THE USERS THAT YOU'RE RUNNING THE CODE AS.
        Specify the tenant ID using the -tenantId argument.
        Specify that you want the affected users removed from their role additionally using the -removeFromRole argument.
        The -whatif switch runs in simulation mode
    .OUTPUTS
        Output to host.
        General log written to '$env:temp\AzureAdTenantTakeBack.log'

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
    Param(
        [Parameter(Mandatory=$true)]
        [string]$exemptedUsers,
        [Parameter(Mandatory=$true)]
        [switch]$removeFromRole,
        [Parameter(Mandatory=$true)]
        [string]$tenantId,
        [Parameter(Mandatory=$false)]
        [switch]$savePasswordsToDisk=$false,
        [Parameter(Mandatory=$false)]
        [switch]$whatIf
    )
    if (!($whatIf)) {
        write-warning "Improper use of this function will lock you out of your tenant and will require Microsoft Support to add you back!"
        write-host "Make sure you've run this at least once with the -whatIf switch to verify actions. Press CTRL+C to cancel"
        write-host "Sleeping 10 seconds for you to have a think..."
        start-sleep 10
    } else {
        write-host "Running in WHATIF mode" -ForegroundColor green
    }
    Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "Beginning Azure AD Tenant Takeback" -logSev "Info" | out-null
    write-warning "This will take some time..."
    Connect-Az -tenantId $tenantId | out-null
    Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "Connected to $($tenantId)" -logSev "Info" | out-null
    #mandatory list of excluded accounts (e.g. break glass, MSIR accounts)
    $exemptedUsersList = @()
    if ($exemptedUsers.Contains(',')) {
        $exemptedUsersList = $exemptedUsers.split(',').trim()
    } else {
        $exemptedUsersList += $exemptedUsers
    }
    if ($exemptedUsersList.length -eq 0){
        write-error "You must exempt users from this process or you will lock yourself out"
        throw
    }
    if (!((Get-AzureADCurrentSessionInfo).account.id -in $exemptedUsersList)) {
        write-error "You must run this as a user in the exempted user list!"
        throw
    }
    Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "Exempted users list is $($exemptedUsersList)" -logSev "Info" | out-null
    Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "Confirming exempted users exist" -logSev "Info" | out-null
    foreach ($eu in $exemptedUsersList){
        #write-output "Confirming exempted user $eu exists"
        $u = Get-AzureADUser -ObjectId $eu
        if ($u) {
            Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "Confirmed exempted user $eu exists with object ID $($u.ObjectId)" -logSev "Info" | out-null
            write-output "Confirmed exempted user $eu exists with object ID $($u.ObjectId)"
        } else {
            Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "FAILED to find exempted user $eu. Critical stop!" -logSev "Error" | out-null
            write-output "FAILED to find exempted user $eu. Critical stop!"
            write-host "======Log file located at $env:temp\AzureAdTenantTakeBack.log======" -ForegroundColor darkmagenta -BackgroundColor white
            throw
        }
    }
    # save it for later
    write-output "Saving report of existing structure"
    Get-AzureAdPrivObjects -tenantId $tenantId -saveReport
    try {
        $report = get-content $env:temp\allAzurePrivObjects.json | ConvertFrom-Json
    } catch {}
    $allUsersList = [System.Collections.Generic.List[AzureObject]]::new()
    #get all your priv users
    # this returns $objList = [System.Collections.Generic.List[AzureObject]]::new()
    write-output "Getting list of directly assigned privileged users"
    $privUsers = Get-AzureAdPrivObjects -tenantId $tenantId -usersOnly
    write-output "Found $($privUsers.count) directly assigned privileged users"
    Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "Found $($privUsers.count) directly assigned privileged users" -logSev "Info" | out-null
    #get all your pim users
    write-output "Checking if PIM is enabled"
    try {
        $pimCheck = Get-AzureADMSPrivilegedRoleAssignment -ProviderId aadRoles -ResourceId $tenantId -erroraction silentlycontinue
        Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "PIM check completed" -logSev "Info" | out-null
    } catch {
        write-warning "PIM does not appear to be enabled."
        Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "PIM does not appear to be enabled." -logSev "Warn" | out-null
    }
    if ($pimCheck) {
        $pimMasterList=@()
        Get-MgDirectoryRole | ? {$_.DisplayName -like "*Administrator" } | % { 
            write-output "Getting list of PIM privileged users for role $($_.DisplayName)"
            try {
                $roleMembers = Get-PIMRoleAssignment -RoleName "$($_.DisplayName)" -tenantId $tenantId
            } catch {
                write-warning "Failed to get list of PIM privileged users for role $($_.DisplayName)"
            }
            
            if ($roleMembers) {
                $pimMasterList += $roleMembers
            }
        }
        write-output "Found $($pimMasterList.count) PIM privileged objects. However this contains duplicates that we will fix shortly."
        Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "Found $($pimMasterList.count) PIM privileged objects. However this contains duplicates that we will fix shortly." -logSev "Info" | out-null
        write-output "Merging direct assigned and PIM lists"
        foreach ($pimObject in $pimMasterList) {
            #you're gonna get groups returned but the function checks them
            #just filter out the (group) lines
            if ($pimObject) {
                try {
                    if (!($pimObject.UserPrincipalName).contains('(Group)')) {
                        $allUsersList.Add([AzureObject]::new($pimObject.ObjectId, $pimObject.UserPrincipalName))
                    }
                } catch {}
            }
        }
        Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "Total PIM count $($allUsersList.count)" -logSev "Info" | out-null
    }
    
    
    # you need to also run through the returned array and build a new list with objid/name IF not contains (group)
    foreach ($privUser in $privUsers) {
        if ($privUser.objId) {
            $allUsersList.Add([AzureObject]::new($privUser.objId, $privUser.objUpn))
        }
    }
    write-output "Removing duplicates"
    $allUsersList = $allUsersList | sort -Unique -Property objId
    write-output "Final count of users is: $($allUsersList.count)"
    Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "Added direct assigned users. Total user count $($allUsersList.count)" -logSev "Info" | out-null
    ######
    # FILTER OUT THE EXEMPTED LIST
    ######
    write-output "Filtering out $($exemptedUsersList.count) exempted users"
    $actionableUsers = $allUsersList.Where({(!($_.objUpn -in $exemptedUsersList))})
    if ($($actionableUsers.count) -gt 1) {
        write-output "We have $($actionableUsers.count) users to operate on!"
    } else {
        write-output "We have $($actionableUsers.count) user to operate on!"
    }
    Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "Filtering out $($exemptedUsersList.count) exempted users" -logSev "Info" | out-null
    Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "We have $($actionableUsers.count) users to operate on!" -logSev "Info" | out-null
    if ($actionableUsers.count -gt 0) {
            write-output "username,newpassword"
            if ($savePasswordsToDisk) {
                "username,password" | out-file $env:temp\UserReset.csv -Append -Force
            }
    }
    ### add in whatif logic here
    if (!($whatif)) {
        #write-host "The -WhatIf parameter was NOT used. The function should process as normal."
        Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "The -WhatIf parameter was NOT used. Running in REAL mode." -logSev "Info" | out-null
        if ($savePasswordsToDisk) {
            if (test-path $env:temp\UserReset.csv) {
                rm $env:temp\UserReset.csv
            }
            new-item -type file $env:temp\UserReset.csv | out-null
        }
        foreach ($actionableUser in $actionableUsers) {
            if ($actionableUser.objId) {
                #only reset password if not #EXT#
                if (!($actionableUser.ObjUpn.Contains('#EXT#'))) {
                    $password = Read-Host "Enter password for $($actionableUser.objUpn)" -AsSecureString
                    if ($savePasswordsToDisk) {
                        "$($actionableUser.objUpn),$password" | out-file $env:temp\UserReset.csv -Append -Force
                    }
                    Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "Operating on user $($actionableUser.objUpn)" -logSev "Info" | out-null
                    
                    Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "Running Command: Set-AzureADUserPassword -ForceChangePasswordNextLogin $true -ObjectId $($actionableUser.objId) -Password  REDACTED" -logSev "Info" | out-null
                    try {
                        Set-AzureADUserPassword -ForceChangePasswordNextLogin $true -ObjectId $($actionableUser.objId) -Password $password
                        Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "Password change successful for $($actionableUser.objUpn)" -logSev "Info" | out-null
                    } catch {
                        $errorMessage = $_.Exception
                        write-error "Failed to reset user password for $($actionableUser.objUpn)"
                        Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "Failed to reset user password for $($actionableUser.objUpn). $errorMessage" -logSev "Error" | out-null
                    }
                }
                Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "Running Command: Revoke-AzureADUserAllRefreshToken -ObjectId $($actionableUser.objId)" -logSev "Info" | out-null
                try {
                    Revoke-AzureADUserAllRefreshToken -ObjectId $($actionableUser.objId)
                    Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "Revoke token successful for $($actionableUser.objUpn)" -logSev "Info" | out-null
                } catch {
                    $errorMessage = $_.Exception
                    write-error "Failed to revoke Azure AD refresh token for $($actionableUser.objId)"
                    Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "Failed to revoke Azure AD refresh token for $($actionableUser.objId). $errorMessage" -logSev "Error" | out-null
                }
                if (($actionableUser.ObjUpn.Contains('#EXT#')) -or $removeFromRole) {
                    #remove from role
                    # if .objUpn.contains('#EXT#') -or $removeFromRole
                    # if it's an external user we remove from role, regardless of variable
                    # if we've said "remove everyone from role" then we do that regardless of '#EXT#'
                    if ($report)
                    {
                        foreach ($roleToRemove in ($report.Where({$_.objDetails.objUpn -match "$($actionableUser.ObjUpn)"}) | select objid)) {
                            Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "Running Command: Remove-AzureADDirectoryRoleMember -ObjectId $($roleToRemove.objId) -MemberId $($actionableUser.objId)" -logSev "Info" | out-null
                            try {
                                Remove-AzureADDirectoryRoleMember -ObjectId $($roleToRemove.objId) -MemberId $($actionableUser.objId)
                                Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "Role removal successful for $($actionableUser.objUpn)" -logSev "Info" | out-null
                            } catch {
                                $errorMessage = $_.Exception
                                write-error "Failed to remove user $($roleToRemove.objId) from role $($actionableUser.objId)"
                                Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "Failed to remove user $($roleToRemove.objId) from role $($actionableUser.objId). $errorMessage" -logSev "Error" | out-null
                            }
                        }
                    } else {
                        write-warning "Unable to remove user $($actionableUser.ObjUpn) because role report not found. Manually remove user from roles."
                        Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "Unable to remove user $($actionableUser.ObjUpn) because role report not found. Manually remove user from roles." -logSev "Warn" | out-null
                    }
                }
            }
        }
        # remove PIM eligible
        $pimListWithoutDupes = $pimMasterList | sort ObjectId, AzureADRoleId -Unique
        foreach ($pimEligible in ($pimListWithoutDupes | ? { $_.PIMAssignment -eq "Eligible" -and $_.AzureAdRoleId -ne $null })) {
            if (!($pimEligible.UserPrincipalName -in $exemptedUsersList)){
                try {
                    Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "Removing pim eligible $($pimEligible.ObjectId) from role $($pimEligible.AzureADRoleId)" -logSev "Info" | out-null
                    $params = @{
                        "principalId" = "$($pimEligible.ObjectId)"
                        "roleDefinitionId" = ((Get-MgDirectoryRoleById -Ids $($pimEligible.AzureADRoleId)).additionalproperties).roleTemplateId
                        "justification" = "AAD Tackback"
                        "directoryScopeId" = "/"
                        "action" = "AdminRemove"
                    }
                    $removeCheck = New-MgRoleManagementDirectoryRoleEligibilityScheduleRequest -BodyParameter $params 
                    write-host "Removing user $($pimEligible.UserPrincipalName) from role $($pimEligible.AzureADRole)"
                    Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "Removing user $($pimEligible.UserPrincipalName) from role $($pimEligible.AzureADRole)" -logSev "Info" | out-null
                } catch {
                    write-error "Failed to remove user $($pimEligible.UserPrincipalName) from role $($pimEligible.AzureADRole)"
                    Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "Failed to remove pim eligible $($pimEligible.ObjectId) from role $($pimEligible.AzureADRoleId)" -logSev "Error" | out-null
                }
            } else {
                write-host "User $($pimEligible.UserPrincipalName) is EXEMPTED and will not be actioned on"
                Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "User $($pimEligible.UserPrincipalName) is EXEMPTED and will not be actioned on" -logSev "Info" | out-null
            }
            
            
        }
     } else {
         Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "The -WhatIf parameter was used. Running in SIMULATION mode." -logSev "Info" | out-null
         foreach ($actionableUser in $actionableUsers) {
            if ($actionableUser.objId) {
                #only reset password if not #EXT#
                if (!($actionableUser.ObjUpn.Contains('#EXT#'))) {
                    $password = Read-Host "Enter password for $($actionableUser.objUpn)" -AsSecureString
                    Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "SIMULATION: Operating on user $($actionableUser.objUpn)" -logSev "Info" | out-null
                    write-output "SIMULATION: Set-AzureADUserPassword -ForceChangePasswordNextLogin $true -ObjectId $($actionableUser.objId) -Password '$password'"
                    Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "SIMULATION: Set-AzureADUserPassword -ForceChangePasswordNextLogin $true -ObjectId $($actionableUser.objId) -Password REDACTED" -logSev "Info" | out-null
                }
                write-output "SIMULATION: Revoke-AzureADUserAllRefreshToken -ObjectId $($actionableUser.objId)"
                Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "SIMULATION: Revoke-AzureADUserAllRefreshToken -ObjectId $($actionableUser.objId)" -logSev "Info" | out-null
                if (($actionableUser.ObjUpn.Contains('#EXT#')) -or $removeFromRole) {
                    #remove from role
                    # if .objUpn.contains('#EXT#') -or $removeFromRole
                    # if it's an external user we remove from role, regardless of variable
                    # if we've said "remove everyone from role" then we do that regardless of '#EXT#'
                    if ($report)
                    {
                        foreach ($roleToRemove in ($report.Where({$_.objDetails.objUpn -match "$($actionableUser.ObjUpn)"}) | select objid)) {
                            #Remove-AzureADDirectoryRoleMember -ObjectId "019ea7a2-1613-47c9-81cb-20ba35b1ae48" -MemberId "c13dd34a-492b-4561-b171-40fcce2916c5"
                            write-output "SIMULATION: Remove-AzureADDirectoryRoleMember -ObjectId $($roleToRemove.objId) -MemberId $($actionableUser.objId)"
                            Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "SIMULATION: Remove-AzureADDirectoryRoleMember -ObjectId $($roleToRemove.objId) -MemberId $($actionableUser.objId)" -logSev "Info" | out-null
                        }
                    } else {
                        # do the manual logic here???
                        write-warning "SIMULATION: Unable to remove user $($actionableUser.ObjUpn) because role report not found. Manually remove user from roles."
                        Add-LogEntry -logFilePath $env:temp\AzureAdTenantTakeBack.log -logMessage "SIMULATION: Unable to remove user $($actionableUser.ObjUpn) because role report not found. Manually remove user from roles." -logSev "Warn" | out-null
                    }
                    
                }
            }
        }
        # remove PIM eligible
        $pimListWithoutDupes = $pimMasterList | sort ObjectId, AzureADRoleId -Unique
        foreach ($pimEligible in ($pimListWithoutDupes | ? { $_.PIMAssignment -eq "Eligible" -and $_.AzureAdRoleId -ne $null })) {
            if (!($pimEligible.UserPrincipalName -in $exemptedUsersList)){
                $params = @{
                    "principalId" = "$($pimEligible.ObjectId)"
                    "roleDefinitionId" = ((Get-MgDirectoryRoleById -Ids $($pimEligible.AzureADRoleId)).additionalproperties).roleTemplateId
                    "justification" = "AAD Tackback"
                    "directoryScopeId" = "/"
                    "action" = "AdminRemove"
                }
                write-host "Removing user $($pimEligible.UserPrincipalName) from role $($pimEligible.AzureADRole)" -ForegroundColor darkcyan
                write-output "SIMULATION: New-MgRoleManagementDirectoryRoleEligibilityScheduleRequest -BodyParameter $params"
            } else {
                write-host "User $($pimEligible.UserPrincipalName) is EXEMPTED and will not be actioned on"
            }
            
        }
     }
     write-host "======Log file located at $env:temp\AzureAdTenantTakeBack.log======" -ForegroundColor darkmagenta -BackgroundColor white
     if ($savePasswordsToDisk) {
        write-host "======Password file located at $env:temp\UserReset.csv======" -ForegroundColor darkred -BackgroundColor white
     }
}
Function Invoke-EntraConnectRemoval
{
  <#
    .SYNOPSIS
    This script provides various functionalities to manage and decommission 
    an Active Directory Connect server.

    .DESCRIPTION
    The script accepts several parameters to perform different actions on the 
    AD Connect server. The available actions include exporting the configuration, 
    stopping the synchronization, disabling AD and Entra accounts, and decommissioning 
    the server.

    .PARAMETER outdir
    Specifies the output directory where the exported files will be saved. If not 
    specified, the output will be written to the current directory.

    .PARAMETER SyncServer
    Specifies the FQDN of the AD Connect server.

    .PARAMETER exportConfig
    Exports the AD Connect configuration to a backup file.

    .PARAMETER stopsync
    Stops the synchronization scheduler on the AD Connect server.

    .PARAMETER DisableADAccount
    Disables the AD connector account on the AD Connect server.

    .PARAMETER DisableEntraAccount
    Disables the Entra connector account on the AD Connect server.

    .PARAMETER DecommissionServer
    Stops the AD Sync service, disables it, and retrieves the computer name and 
    domain of the AD Connect server.

    .PARAMETER AllActions
    Performs all the actions mentioned above.

    .EXAMPLE
    .\Invoke-EntraConnectRemoval.ps1 -SyncServer "adconnect.contoso.com" -exportConfig -outdir "C:\Backup"

    Exports the AD Connect configuration to a backup file and saves it in the "C:\Backup" directory.

    .EXAMPLE
    .\Invoke-EntraConnectRemoval.ps1 -SyncServer "adconnect.contoso.com" -stopsync

    Stops the synchronization scheduler on the AD Connect server.

  #>


  [CmdletBinding()]
  param (
      # Output directory. If not specified the output will be written to the current directory
      [Parameter()]
      [string]
      $outdir,

      # AD Connect FQDN
      [Parameter(Mandatory=$true)]
      [string]
      $SyncServer,

      [Parameter()]
      [switch]
      $exportConfig,

      [Parameter()]
      [switch]
      $stopsync,

      [Parameter()]
      [switch]
      $DisableADAccount,

      [Parameter()]
      [switch]
      $DisableEntraAccount,

      [Parameter()]
      [switch]
      $DecommissionServer,

      [Parameter()]
      [switch]
      $AllActions
  )

  if ($outdir)
  {
    # check if outdir exists
    if (-not (Test-Path $outdir))
    {
      New-Item -Path $outdir -ItemType Directory
    }
  } else {
    $outdir = Split-Path ($MyInvocation.MyCommand.Path) -parent
  }

  #region checkTargetServer
  # Check if AD Connect server is reachable
  if ($SyncServer)
  {
    if (-not(test-wsman -ComputerName $SyncServer))
    {
      Write-Error -Message "Could not connect to $SyncServer"
      exit
    }
    
    # Check if AD Sync server
    if (-not (Invoke-Command -ComputerName $SyncServer -ScriptBlock {Get-Service ADSync -ErrorAction SilentlyContinue}))
    {
      Write-Error -Message "Specified server is not running ad connect"
      exit
    }
  }
  #endregion checkTargetServer

  #region ExportConfig
  if ($ExportConfig -or $AllActions)
  {
    $r = invoke-command -ComputerName $SyncServer -ScriptBlock {
      $programData = $Env:ProgramData
      
      <# 
      #This section will dump the last run results. It doesnt work because of credssp in the remote
      #session. It throws a SOAP error. Will fix later.

      $adcpath = (Get-ItemProperty -Path "HKLM:\Software\Microsoft\Azure AD Connect").installationpath

      #Import AD Connect module
      try {
        Import-Module "$adcpath\Tools\AdSyncTools.psm1"
      } catch {
        Write-Error -Message "Could not import ADSync module"
        exit
      }

      Export-ADSyncToolsRunHistory -targetname $programData\AADConnect\RunHistory.xml
      #>

      Compress-Archive -Path $programData\AADConnect -DestinationPath $programData\AADConnectBackup.zip -Force
      return "$programData\AADConnectBackup.zip"
    }

    $filename = $r.split("\")[$r.split("\").count-1]
    $Session = New-PSSession -ComputerName $SyncServer
    Copy-Item $r -Destination $outdir\$filename -FromSession $Session
    $session | Remove-PSSession

    #connect remote posh to ADSync server
    $ADConnectOUList = Invoke-Command -ComputerName $SyncServer -ScriptBlock {

      Import-Module "C:\Program Files\Microsoft Azure AD Sync\Bin\ADSync\ADSync.psd1"
      $ADSyncADConnectorSet = (Get-ADSyncConnector | Where-Object {$_.Type -eq "AD"})
      $SyncedOUTable = @()

      Foreach ($Connector in $ADSyncADConnectorSet)
      {
          # loop through connectors
          <#
              $Connector.Name is the forest name
              $connector.partitions are the synced domains in the forest
          #>

          # Loop through partitions in the connector
          Foreach ($Partition in $connector.Partitions)
          {
              $results = [PSCustomObject]@{
                  # The inclusions show only the selected top level OUs. To get the full list of selected OUs you 
                  # have to loop through the OUs in the domain and remove the exclusions from that list.
                  connectorName = $Connector.name
                  domainName = $Partition.name
                  Exclusions = $Partition.ConnectorPartitionScope.ContainerExclusionList
                  Inclusions = $Partition.ConnectorPartitionScope.ContainerInclusionList
              }
              $SyncedOUTable += $Results
          }
      }

      Return $SyncedOUTable
    }
    # Loop through the connected domains
    $allDomainSyncInfo = @()

    Foreach ($Domain in $ADConnectOUList)
    {
      # Go through all the domains to parse the local OU's and add them to an array
      #$LocalOU = Get-ADOrganizationalUnit -Filter * -Properties Distinguishedname -server $domain.domainName | Select-Object Distinguishedname
      $fqdn = $domain.domainName
      $defaultNamingContext = ([ADSI] "LDAP://$fqdn/RootDSE").get("DefaultNamingContext")
      $defaultNamingContext = [ADSI]"LDAP://$defaultNamingContext"
      $adsisearcher = New-Object system.directoryservices.directorysearcher($defaultNamingContext)
      $adsisearcher.filter = "(&(objectClass=organizationalUnit))"
      $LocalOU = ($adsisearcher.FindAll().path).replace("LDAP://","")

      [System.Collections.ArrayList]$IncludedOUList = @()

      Foreach ($IncludedOU in $domain.Inclusions)
      {
        $LocalOU | ForEach-Object { if($_ -match "$IncludedOU") {$IncludedOUList.Add("$_")}}
      }

      foreach ($ExcludedOU in $domain.Exclusions)
      {
        $IncludedOUList.Remove($ExcludedOU)
      }

      $domainSyncInfo = [PSCustomObject]@{
          connectorName = $domain.connectorName
          domainName = $domain.domainName
          syncedOU = $IncludedOUList
      }

      $allDomainSyncInfo += $domainSyncInfo
    }

    $allDomainSyncInfo | ConvertTo-Json | out-file -filepath $outdir\ADSyncOUList.txt

  }
  #endregion ExportConfig

  #region StopSync
  if ($stopsync -or $AllActions)
  {
    $r = invoke-command -ComputerName $SyncServer -ScriptBlock {
      # Find AD Connect install path
      $adconnectpath = (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\AD Sync').location

      #Import AD Connect module
      try {
        Import-Module "$adconnectpath\bin\ADSync\ADSync.psd1"
      } catch {
        Write-Error -Message "Could not import ADSync module"
        exit
      }
    
      # Stop scheduler
      Set-ADSyncScheduler -SyncCycleEnabled $false
      Return (Get-ADSyncScheduler)
    }

    $r | Out-File "$outdir\ADSyncScheduler.txt"

  }
  #endregion StopSync

  #region DisableADAccount
  if ($DisableADAccount -or $AllActions)
  {
    $r = invoke-command -ComputerName $SyncServer -ScriptBlock {
      # Find AD Connect install path
      $adconnectpath = (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\AD Sync').location

      #Import AD Connect module
      try {
        Import-Module "$adconnectpath\bin\ADSync\ADSync.psd1"
      } catch {
        Write-Error -Message "Could not import ADSync module"
        exit
      }

      # Remove connector account AD
      $connectors = Get-ADSyncConnector
      $adconnector = Get-ADSyncConnector | Where-Object {$_.ConnectorTypeName -eq "AD"}
      $MAUser = ($adconnector.connectivityparameters | Where-Object {$_.name -eq "forest-login-user"}).value
      $MAUserDomain = ($adconnector.connectivityparameters | Where-Object {$_.name -eq "forest-login-Domain"}).value

      return $MAUser,$MAUserDomain, $connectors
    }

    $MAUser = $r[0]
    $MAUserDomain = $r[1]
    $r[2] | Out-File $outdir\ADSyncConnectors.txt

    $dnc = ([ADSI] "LDAP://$MAUserDomain/RootDSE").get("DefaultNamingContext")
    $dnc = [ADSI]"LDAP://$dnc"
    $adsisearcher = New-Object system.directoryservices.directorysearcher($dnc)
    $adsisearcher.filter = "(&(objectClass=user)(sAMAccountName=$MAUser))"
    $adsisearcher.FindAll() | ForEach-Object {
      Write-Host "Disabling user " $_.path -ForegroundColor Cyan
      try{
        $u = [adsi]$_.path
        $u.psbase.InvokeSet("AccountDisabled", $true)
        $u.setinfo()
      } catch {
        Write-Host "Could not disable user $MAUser please disable this user manually" -ForegroundColor Magenta
      }
    }

  }
  #endregion DisableADAccount

  #region DisableEntraAccount
  If ($DisableEntraAccount -or $AllActions)
  {
    $r = invoke-command -ComputerName $SyncServer -ScriptBlock {
      # Find AD Connect install path
      $adconnectpath = (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\AD Sync').location

      #Import AD Connect module
      try {
        Import-Module "$adconnectpath\bin\ADSync\ADSync.psd1"
      } catch {
        Write-Error -Message "Could not import ADSync module"
        exit
      }

      $EntraConnector = Get-ADSyncConnector | Where-Object {$_.subtype -eq "Windows Azure Active Directory (Microsoft)"}
      $tenantid = $entraconnector.name.split("-")[0].trim()
      $syncuser = ($EntraConnector.connectivityparameters | Where-Object {$_.name -eq "UserName"}).value

      return $syncuser,$tenantid
    }

    $syncuser = $r[0]
    $tenantid = $r[1]

    $body = @{
      accountEnabled = $false
    }

    Update-EntraObject -objectType user -objectId $syncuser -postBody $body

    write-host "Disabled $syncuser" -ForegroundColor Cyan

    #$uri = "https://graph.microsoft.com/v1.0/users?`$filter=startswith(displayName,'On-Premises Directory Synchronization Service Account') and accountEnabled eq true"
    $filter = "startswith(displayName,'On-Premises Directory Synchronization Service Account') and accountEnabled eq true"

    $r = Get-EntraObject -objectType user -filter $filter

    if (($r.content | convertfrom-json).value.count -gt 1)
    {
      $confirm = Read-Host "Multiple enabled sync accounts found. Would you like to disable all of them? [Y/N]" -ForegroundColor Yellow
      If ($confirm -ieq "Y")
      {
        ForEach ($u in ($r.content | convertfrom-json).Value) 
        {
          Update-EntraObject -objectType user -objectId $u.id -postBody $body
          write-host "Disabled $($u.userPrincipalName)" -ForegroundColor Magenta
        }
      } else {
        write-host "Chicken" 
      }
    }

  }
  #endregion DisableEntraAccount

  #region DecommissionServer
  if ($DecommissionServer -or $AllActions)
  {
    $r = invoke-command -ComputerName $SyncServer -ScriptBlock {
      Get-Service ADSync | Stop-Service -Force
      Set-Service ADSync -StartupType Disabled

      $cmpname = (Get-ChildItem env:computername).value
      $cmpdomain = ([System.DirectoryServices.ActiveDirectory.Domain]::GetComputerDomain()).name.trim()

      return $cmpname, $cmpdomain
    }

    $SyncSrvName = $r[0]
    $SyncSrvDomain = $r[1]

    $dnc = ([ADSI] "LDAP://${SyncSrvDomain}/RootDSE").get("DefaultNamingContext")
    $dnc = [ADSI]"LDAP://${dnc}"
    $adsisearcher = New-Object system.directoryservices.directorysearcher(${dnc})
    $adsisearcher.filter = "(&(objectClass=computer)(Name=${SyncSrvName}))"
    $adsisearcher.FindAll() | ForEach-Object {
      Write-Host "Disabling ADSync Computer " $_.path -ForegroundColor Cyan
      try{
        $c = [adsi]$_.path
        $c.psbase.InvokeSet("AccountDisabled", $true)
        $c.setinfo()
      } catch {
        Write-Host "Could not disable sync server ${SyncSrvName} please disable manually" -ForegroundColor Magenta
      }
    }
  }
  #endregion DecommissionServer

} # End of function Invoke-EntraConnectRemoval


function Invoke-EntraMassPasswordReset 
{
  [CmdletBinding()]
  param (
      [Parameter(mandatory = $true)]
      [string]
      $tenantid,

      [Parameter()]
      [string]
      $exemptedUsers
  )

  # Check if users have MFA methods configured
  # Check If sspr is enabled
  # Reset password for users
  # User exclusion list

  #https://learn.microsoft.com/en-us/graph/api/authenticationmethod-resetpassword?view=graph-rest-1.0&tabs=http
  $Scopes = "UserAuthenticationMethod.ReadWrite.All"

  try {
    Connect-MgGraph -tenantid $tenantid -Scopes $scopes  
  } catch {
    Write-Error -Message "Failed to connect to Microsoft Graph API. $_"
    exit
  }


  # Get all users

  # this is only an inclusive filter, cannot be inverted. Keeping for future reference.
  # https://graph.microsoft.com/v1.0/users?$select=id,userprincipalname&$filter=notendsWith(userPrincipalName,'%23EXT%23@petsandcattle.onmicrosoft.com')

  $users = @()
  $uri = "https://graph.microsoft.com/v1.0/users?`$select=id,userprincipalname"
  $graphResults = (Invoke-MgGraphRequest -Method GET -Uri $uri)
  $users += $graphResults.value

  if ($graphResults.'@odata.nextLink') 
  {
    do {
      $graphResults = (Invoke-MgGraphRequest -Uri $graphResults.'@odata.nextLink' )
      $users += $graphResults.value
    } until (
      -not($graphResults.'@odata.nextLink')
    )
  }

  # have to do this abomination because Graph cannot make an exclusive filter
  # And internal user can be type guest and a b2b user can be type member. Must query on UPN containing #ext#
  Foreach ($user in ($users | Where-object {$_.userPrincipalName -inotlike "*#EXT#*"})) 
  {
    If (-not $exemptedUsers.Contains($user.userPrincipalName))
    {
      $userID = $user.id
      $uri = "https://graph.microsoft.com/v1.0/users/$userID/authentication/passwordMethods"
      $pwmethod = (Invoke-MgGraphRequest -Method GET -Uri $uri).value.id
      $uri = "https://graph.microsoft.com/v1.0/users/$userID/authentication/methods/$pwmethod/resetPassword"

      <# Can use this in case you want to generate passwords and export to a file.
      $postbody = @{
        "newPassword" = -join ((33..122) | Get-Random -Count 14 | ForEach-Object {[char]$_})
      }
      #>
      
      $postbody = @{} # Empty postbody so Entra will generate a random password
      
      Invoke-MgGraphRequest -Method POST -Uri $uri -Body $postbody | Out-Null
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
function Invoke-MDISetup {
    [CmdletBinding(DefaultParameterSetName="servers")]
    [OutputType([bool])]
    Param(
        [parameter(Mandatory=$true)]
        [string]$path,
        [Parameter(Mandatory=$true)]
        [string]$accessKey,
        [Parameter(Mandatory = $false,ValueFromPipeline = $true,ParameterSetName='pipeline')]
        $pipelineInput,
        [parameter(ParameterSetName="servers")]
        [string[]]$server=$(Get-WmiObject win32_computersystem | %{ $_.DNSHostName + "." + $_.Domain}),
        [Parameter(ParameterSetName="allDCs")]
        [switch]$allDomainControllers,
        [Parameter(Mandatory=$false)]
        [switch]$setServerReadiness,
        [Parameter(Mandatory=$false)]
        [switch]$useProxy,
        [Parameter(Mandatory=$false)]
        [string]$proxyUrl="",
        [Parameter(DontShow)]
        [string]$domain
    )
    begin {
        $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain  
        $actionableServers = [System.Collections.Generic.List[String]]::new()
        $jobArray = @()
    }
    process {
        if ($PSCmdlet.ParameterSetName -eq 'pipeline') {
            if ($pipelineInput.isReady) {
                $actionableServers.Add($($pipelineInput.server))
            } else {
                write-warning "$($pipelineInput.server)`: is reporting as not ready"
                Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($pipelineInput.server)`: is reporting as not ready" -logSev "Warn" | out-null
                if ($setServerReadiness) {
                    write-host "$($pipelineInput.server)`: Attempting to set readiness"
                    Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($rs.server)`: Attempting to set readiness" -logSev "Info" | out-null
                    if (Set-MDIServerReadiness -server $($pipelineInput.server)) {
                        write-host "$($pipelineInput.server)`: has been set to ready"
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($pipelineInput.server)`: has been set to ready" -logSev "Info" | out-null
                        $actionableServers.Add($($pipelineInput.server))
                    } else {
                        write-warning "$($pipelineInput.server)`: failed to be set to ready and will be skipped"
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($rs.server)`: failed to be set to ready" -logSev "Warn" | out-null
                    }
                }
            }
        }
        if ($PSCmdlet.ParameterSetName -eq 'servers') {
            if ($server -match ',') {
                ($server.split(',')) | % {
                    $actionableServers.Add($_)
                }
            } else {
                if ($server.count -gt 1) {
                    $server | % { 
                        $actionableServers.Add($_)
                    }
                } 
                if ($server) {
                    $actionableServers.Add($server)
                }
            }
        }
        if ($allDomainControllers) {
            $myDomain.domainDetail.ReplicaDirectoryServers | % { 
                $actionableServers.Add($_)
            }
        }
        foreach ($actionableServer in $actionableServers) {
            if (!(Test-MDIIsInstalled -server $actionableServer)) {
                # check for path exist
                $c = 'test-path "'+$path+'"'
                $command  = "Invoke-command -scriptblock {$c} -ComputerName $actionableServer"+' 2> $null'
                $testPathCommand = iex $command
                if ($testPathCommand) {
                    Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$actionableServer`: Successfully tested $path." -logSev "Info" | out-null
                    # test endpoint connection
                    $portalComsCheck = Test-MDISensorApiConnection -path $path -server $actionableServer -proxyUrl $proxyUrl
                    if ($portalComsCheck) {
                        write-output "$actionableServer`: Successfully connected to sensor API endpoint"
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$actionableServer`: Successfully connected to sensor API endpoint" -logSev "Info" | out-null
                    } else {
                        write-warning "$actionableServer`: Failed to connect to sensor API endpoint. Install will proceed, but may not succeed"
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$actionableServer`: Failed to connect to sensor API endpoint. Install will proceed, but may not succeed" -logSev "Warn" | out-null
                    }

                    # flush kerb tickets
                    try {
                        $c = 'cmd /c klist -li 0x3e7 purge'
                        $command  = "Invoke-command -scriptblock {$c} -ComputerName $actionableServer"+' 2> $null'
                        $kerbCommand = iex $command
                        write-output "$actionableServer`: Purged kerberos tickets"
                        start-sleep 2
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$actionableServer`: Purged kerberos tickets" -logSev "Info" | out-null
                    } catch {
                        $errorMessage = $_.Exception
                        write-error "$actionableServer`: Failed to purge kerberos tickets"
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$actionableServer`: Failed to purge kerberos tickets. $errorMessage" -logSev "Error" | out-null
                    }

                    # install mdi
                    $mdiInstallString=""
                    if ($useProxy) {
                        $mdiInstallString='"{0}\Azure ATP sensor Setup.exe" /quiet NetFrameworkCommandLineArguments="/q" AccessKey="{1}" ProxyUrl="{2}"' -f $path,$accessKey,$proxyUrl
                    } else {
                        $mdiInstallString='"{0}\Azure ATP sensor Setup.exe" /quiet NetFrameworkCommandLineArguments="/q" AccessKey="{1}"' -f $path,$accessKey
                    }
                    write-output "Installing MDI with string $mdiInstallString"
                    Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$actionableServer`: Installing MDI with string $mdiInstallString" -logSev "Info" | out-null
                    try {
                        $jobArray += start-job -scriptblock {param ($as,$mdiInstallString)
                            $c = 'cmd /c '+$mdiInstallString
                            $command  = "Invoke-command -scriptblock {$c} -ComputerName $as"+' 2> $null'
                            $mdiCommand = iex $command
                        
                        } -ArgumentList $actionableServer,$mdiInstallString
                    } catch {
                        write-error $_.ExceptionMessage
                        write-error "$actionableServer`: Failed to install MDI"
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$actionableServer`: Failed to install MDI. $errorMessage" -logSev "Error" | out-null
                        #throw
                    }
                } else {
                    write-error "Failed to find $path"
                    Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to find $path." -logSev "Error" | out-null
                }
            } else {
                write-output "$actionableServer`: MDI appears to be installed already here. Skipping."
            }
        }
        foreach ($actionableServer in $actionableServers) {
            if (!(Test-MDIIsInstalled -server $actionableServer)) {
                ## this needs to be a new loop with multithreading
                $jobArray += Start-Job -Scriptblock {param ($as) 
                    # make sure setup is done before checking for service start
                    # we need to look for the process by name of Azure ATP Sensor Setup.exe, but there are 2 that launch.
                    # get a process list, if "Azure ATP Sensor Setup" -in (get-process).name enter loop, sleep 3, check again
                    $maxChecks=6
                    $checkCount=0
                    $c = '"Azure ATP Sensor Setup" -in (get-process).name'
                    $command  = "Invoke-command -scriptblock {$c} -ComputerName $as"+' 2> $null'
                    $mdiSetupStillRunning = iex $command
                    while ($mdiSetupStillRunning) {
                        if ($checkCount -gt $maxChecks) {
                            write-warning "$actionableServer`: We checked for MDI setup $checkCount times and it was still running. This exceeds max check count of $maxChecks"
                            break
                        }
                        start-sleep 10
                        $mdiSetupStillRunning = iex $command
                        $checkCount++
                    }
                    # check for service start
                    write-output "$as`: Checking start status for MDI."
                    $mdiStatus = (get-service AATPSensor -computername $as -erroraction silentlycontinue)
                    if (!($mdiStatus)) {
                        write-output "$as`: Sleeping for start..."
                        start-sleep 10
                        $mdiStatus = (get-service AATPSensor -computername $as -erroraction silentlycontinue)
                        write-output "$as`: Sleeping again...."
                        start-sleep 30
                        $mdiStatus = (get-service AATPSensor -computername $as -erroraction silentlycontinue)
                    }
                    if ($mdiStatus) {
                        $x=0
                        while (($mdiStatus.Status -ne "Running") -and ($x -le 60)) {
                            $mdiStatus = (get-service AATPSensor -computername $as -erroraction silentlycontinue)
                            $x++
                            start-sleep 1
                        }
                        $ms = $mdiStatus.Status
                        if ($mdiStatus.Status -ne "Running") {
                                write-warning "$as`: MDI service failed to start"
                                write-warning "View logfiles at C:\Program Files\Azure Advanced Threat Protection Sensor\<VERSION>\Logs"
                        } else {
                            write-output "$as`: MDI service is $ms"
                        }
                    } else {
                        write-error "$as`: Failed to get service status for MDI."
                    }
                } -ArgumentList $actionableServer
            }
        }

        # do not remove or move this
        if ($PSCmdlet.ParameterSetName -eq 'pipeline') {
            $actionableServers.Remove($($pipelineInput.server)) | out-null
        }
    }
    end {
        #clean up the threads here
        $maxWaitTime=90
        $checks=0
        write-host "Waiting for background jobs to finish"
        while (((get-job | ? { $_.state -ne 'Completed'}).count -gt 0)) {
            if ($checks -gt $maxWaitTime) {
                break
            }
            start-sleep 1
            $checks++
        }
        get-job | receive-job
        $checks=0
        while (((get-job | ? { $_.HasMoreData -eq 'True'}).count -gt 0)) {
            if ($checks -gt $maxWaitTime) {
                break
            }
            get-job | receive-job
            start-sleep 1
            $checks++
        }
    }
}
function New-ClawFineGrainedPasswordPolicy {


    [CmdletBinding()]
    Param(
        [Parameter(mandatory=$false)]
        [int]$maxPasswordAge=90,
        [Parameter(mandatory=$false)]
        [int]$minPasswordLength=25,
        [Parameter(mandatory=$false)]
        [int]$passwordHistoryCount=12,
        [Parameter(mandatory=$false)]
        [bool]$complexityEnabled=$true,
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )
    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
    if (!($mydomain.domainDetail.DomainMode -like "*2003*" -or $mydomain.domainDetail.DomainMode -like "*2000*")) {
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Domain mode passed check for fine grained password ability" -logSev "Info" | out-null
        write-verbose "Checking for required groups"
        # do a check here for tier 0 operators before blindly running new-clawgroup
        $group = $null
        try {
            $group = $myDomain.GetObjectByName("Tier 0 Operators","domain",$false)
        }
        catch {}
        if (!($group)) {
            New-ClawGroup
        }
        try {
            if ($myDomain.credential -eq [System.Management.Automation.PSCredential]::Empty) {
                $x = Get-ADFineGrainedPasswordPolicy -Identity "Tier 0 Password Policy"  -Server $myDomain.chosenDc -ErrorAction silentlycontinue
            } else {
                $x = Get-ADFineGrainedPasswordPolicy -Identity "Tier 0 Password Policy"  -Server $myDomain.chosenDc -Credential $myDomain.credential -ErrorAction silentlycontinue
            }
            
            if ($x) {
                $pwdPolicyExists = $true
            } else {
                $pwdPolicyExists = $false
            }
        } catch {
            $pwdPolicyExists = $false
        }
        if (!($pwdPolicyExists)) {
            try {
                if ($myDomain.credential -eq [System.Management.Automation.PSCredential]::Empty) {
                    New-ADFineGrainedPasswordPolicy "Tier 0 Password Policy" -ComplexityEnabled:$complexityEnabled -LockoutDuration:"00:30:00" -LockoutObservationWindow:"00:30:00" -LockoutThreshold:"0" -MaxPasswordAge:"$($maxPasswordAge).00:00:00" -MinPasswordAge:"1.00:00:00" -MinPasswordLength:"$minPasswordLength" -PasswordHistoryCount:"$passwordHistoryCount" -Precedence:"1" -ReversibleEncryptionEnabled:$false -ProtectedFromAccidentalDeletion:$true -Server $myDomain.chosenDc
                } else {
                    New-ADFineGrainedPasswordPolicy "Tier 0 Password Policy" -ComplexityEnabled:$complexityEnabled -LockoutDuration:"00:30:00" -LockoutObservationWindow:"00:30:00" -LockoutThreshold:"0" -MaxPasswordAge:"$($maxPasswordAge).00:00:00" -MinPasswordAge:"1.00:00:00" -MinPasswordLength:"$minPasswordLength" -PasswordHistoryCount:"$passwordHistoryCount" -Precedence:"1" -ReversibleEncryptionEnabled:$false -ProtectedFromAccidentalDeletion:$true -Server $myDomain.chosenDc -Credential $myDomain.credential
                }
                write-host "Created fine grained password policy Tier 0 Password Policy"
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Created fine grained password policy Tier 0 Password Policy" -logSev "Info" | out-null
            } catch {
                $errorMessage = $_.Exception
                write-error "Failed to create fine grained password policy Tier 0 Password Policy"
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to create fine grained password policy Tier 0 Password Policy. $errorMessage" -logSev "Error" | out-null
            }
        }
        # check for who is in the policy "Tier 0 Operators" -in (Get-ADFineGrainedPasswordPolicySubject -Identity "Tier 0 Password Policy").name
        try {
            $daGroupName = $myDomain.privilegedGroupNames["Domain Admins"]
            if ($myDomain.credential -eq [System.Management.Automation.PSCredential]::Empty) {
                $groupList = (Get-ADFineGrainedPasswordPolicySubject -Identity "Tier 0 Password Policy" -Server $myDomain.chosenDc).name
            } else {
                $groupList = (Get-ADFineGrainedPasswordPolicySubject -Identity "Tier 0 Password Policy" -Server $myDomain.chosenDc -Credential $myDomain.credential).name
            }
            if (!("$daGroupName" -in $groupList)) {
                if ($myDomain.credential -eq [System.Management.Automation.PSCredential]::Empty) {
                    Add-ADFineGrainedPasswordPolicySubject -Identity "Tier 0 Password Policy" -Subjects "$daGroupName" -Server $myDomain.chosenDc
                } else {
                    Add-ADFineGrainedPasswordPolicySubject -Identity "Tier 0 Password Policy" -Subjects "$daGroupName" -Server $myDomain.chosenDc -Credential $myDomain.credential
                }
                
                write-host "Added $daGroupName to policy Tier 0 Password Policy"
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Added $daGroupName to policy Tier 0 Password Policy" -logSev "Info" | out-null
            }
        } catch {
            $errorMessage = $_.Exception
            write-error "Failed to add $daGroupName to policy Tier 0 Password Policy"
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to add $daGroupName to policy Tier 0 Password Policy. $errorMessage" -logSev "Error" | out-null
        }
        try {
            if ($myDomain.credential -eq [System.Management.Automation.PSCredential]::Empty) {
                $groupList = (Get-ADFineGrainedPasswordPolicySubject -Identity "Tier 0 Password Policy" -Server $myDomain.chosenDc).name
            } else {
                $groupList = (Get-ADFineGrainedPasswordPolicySubject -Identity "Tier 0 Password Policy" -Server $myDomain.chosenDc -Credential $myDomain.credential).name
            }
            if (!("Tier 0 Operators" -in $groupList)) {
                if ($myDomain.credential -eq [System.Management.Automation.PSCredential]::Empty) {
                    Add-ADFineGrainedPasswordPolicySubject -Identity "Tier 0 Password Policy" -Subjects "Tier 0 Operators" -Server $myDomain.chosenDc
                } else {
                    Add-ADFineGrainedPasswordPolicySubject -Identity "Tier 0 Password Policy" -Subjects "Tier 0 Operators" -Server $myDomain.chosenDc -Credential $myDomain.credential
                }
                write-host "Added Tier 0 Operators to policy Tier 0 Password Policy"
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Added Tier 0 Operators to policy Tier 0 Password Policy" -logSev "Info" | out-null
            }
            
        } catch {
            $errorMessage = $_.Exception
            write-error "Failed to add Tier 0 Operators to policy Tier 0 Password Policy"
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to add Tier 0 Operators to policy Tier 0 Password Policy. $errorMessage" -logSev "Error" | out-null
        }
        
    } else {
        write-warning "Domain mode insufficient for fine grained password. Current mode is $($myDomain.domainDetail.DomainMode)"
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Domain mode insufficient for fine grained password. Current mode is $($myDomain.domainDetail.DomainMode)" -logSev "Warn" | out-null
    }
    
}
function New-ClawGpo
{

    ##########################################################################################################
    <#
    .SYNOPSIS
        Creates CLAW GPO structure.
    
    .DESCRIPTION
        Creates CLAW GPO structure

    .EXAMPLE
        New-ClawGpo -Domain halo.net

        Creates the required GPO's for CLAW

    .OUTPUTS
        Verbose output to host.
        General log written to '$env:temp\claw.log'

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

    ###################################
    ## Function Options and Parameters
    ###################################

    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [parameter(Mandatory=$false,DontShow)]
        [switch]$FullTier=$false,
        [Parameter(Mandatory=$false)]
        [string]$IDOUName="SITH",
        [Parameter(Mandatory=$false)] # make sure to add a reference for any new GPO here
        [ValidateSet('MachineAccountPassword','Tier0BaselineAudit','Tier0DisallowDSRMLogin','Tier0DomainBlock','Tier0DomainControllers','Tier0ESXAdminsRestrictedGroup','Tier0UserRightsAssignments','Tier0RestrictedGroups','Tier1LocalAdminSplice','Tier1UserRightsAssignments','Tier1RestrictedGroups',
            'Tier2LocalAdminSplice','Tier2UserRightsAssignments','Tier2RestrictedGroups','TierAllDisableSMB1','TierAllDisableWdigest','All')] [string[]]$gposToCreate = "All",
        [parameter(Mandatory=$false)]
        [switch]$Force,
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )
    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Entering function New-ClawGpo" -logSev "Info" | out-null
    write-verbose "Entering function New-ClawGpo"
    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
    if ([string]::IsNullOrEmpty($myDomain.writableSysvolPath)) {
        write-error "Failed to discover SYSVOL. See $env:temp\claw.log for details. Critical stop!"
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to discover SYSVOL $($_.Exception)" -logSev "Error" | out-null
        throw
    }
    # discover AD
    $clawGpoGroups = Get-ClawGpoGroups -domain $domain -myDomain $myDomain

    # make sure the claw groups exist
    try {
        if ([string]::IsNullOrEmpty($clawGpoGroups["tier0Operators"]) -or [string]::IsNullOrEmpty($clawGpoGroups["tier1Operators"]) -or [string]::IsNullOrEmpty($clawGpoGroups["tier2Operators"])) {
            throw
        }
    } catch {
        write-warning "Missing groups detected. Running New-ClawGroup"
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Missing groups detected. Running New-ClawGroup" -logSev "Warn" | out-null
        New-ClawGroup -myDomain $myDomain -IDOUName $IDOUName
        $clawGpoGroups = Get-ClawGpoGroups -domain $domain -myDomain $myDomain
    }
    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Discovered: Domain Admins: $($clawGpoGroups["domainAdmins"]), Schema Admins: $($clawGpoGroups["schemaAdmins"]), Enterprise Admins: $($clawGpoGroups["enterpriseAdmins"])" -logSev "Info" | out-null
    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Discovered: Enterprise Read Only Domain Controllers: $($clawGpoGroups["enterpriseReadOnlyDomainControllers"]), Read-only Domain Controllers: $($clawGpoGroups["readOnlyDomainControllers"])" -logSev "Info" | out-null
    # this line exists to get the manifest names for the GPO so we can keep the names in 1 place (the ClawDomainReadiness class) and not have to update them everywhere
    [ClawDomainReadiness]$myClawDomainReadiness = [ClawDomainReadiness]::new($myDomain)
    $clawGpoList = [System.Collections.Generic.List[ClawGpo]]::new()
    #GPOs to be created
    # if we fulltier with ALL then that's full auto
    # if we specify a tier 1/2 gpo then fulltier MUST be set
    if (Use-GpoName $gposToCreate 'Tier0BaselineAudit') {$clawGpoList.Add([ClawGpo]::new("$($myClawDomainReadiness.Tier0BaselineAudit.GpoName)")) | out-null}
    if (Use-GpoName $gposToCreate 'Tier0DisallowDSRMLogin') {$clawGpoList.Add([ClawGpo]::new("$($myClawDomainReadiness.Tier0DisallowDSRMLogin.GpoName)")) | out-null}
    if (Use-GpoName $gposToCreate 'Tier0DomainBlock') {$clawGpoList.Add([ClawGpo]::new("$($myClawDomainReadiness.Tier0DomainBlock.GpoName)")) | out-null}
    if (Use-GpoName $gposToCreate 'Tier0DomainControllers') {$clawGpoList.Add([ClawGpo]::new("$($myClawDomainReadiness.Tier0DomainControllers.GpoName)")) | out-null}
    if (Use-GpoName $gposToCreate 'Tier0ESXAdminsRestrictedGroup') {$clawGpoList.Add([ClawGpo]::new("$($myClawDomainReadiness.Tier0ESXAdminsRestrictedGroup.GpoName)")) | out-null}
    if (Use-GpoName $gposToCreate 'Tier0RestrictedGroups') {$clawGpoList.Add([ClawGpo]::new("$($myClawDomainReadiness.Tier0RestrictedGroups.GpoName)")) | out-null}
    if (Use-GpoName $gposToCreate 'Tier0UserRightsAssignments') {$clawGpoList.Add([ClawGpo]::new("$($myClawDomainReadiness.Tier0UserRightsAssignments.GpoName)")) | out-null}
    if (Use-GpoName $gposToCreate 'TierAllDisableSMB1') {$clawGpoList.Add([ClawGpo]::new("$($myClawDomainReadiness.TierAllDisableSMB1.GpoName)")) | out-null}
    if (Use-GpoName $gposToCreate 'TierAllDisableWdigest') {$clawGpoList.Add([ClawGpo]::new("$($myClawDomainReadiness.TierAllDisableWdigest.GpoName)")) | out-null}
    if (Use-GpoName $gposToCreate 'MachineAccountPassword') {$clawGpoList.Add([ClawGpo]::new("$($myClawDomainReadiness.MachineAccountPassword.GpoName)")) | out-null}
    if ($FullTier -or ('All' -notin $gposToCreate)) {
        if (Use-GpoName $gposToCreate 'Tier1RestrictedGroups') {$clawGpoList.Add([ClawGpo]::new("$($myClawDomainReadiness.Tier1RestrictedGroups.GpoName)")) | out-null}
        if (Use-GpoName $gposToCreate 'Tier1UserRightsAssignments') {$clawGpoList.Add([ClawGpo]::new("$($myClawDomainReadiness.Tier1UserRightsAssignments.GpoName)")) | out-null}
        if (Use-GpoName $gposToCreate 'Tier2RestrictedGroups') {$clawGpoList.Add([ClawGpo]::new("$($myClawDomainReadiness.Tier2RestrictedGroups.GpoName)")) | out-null}
        if (Use-GpoName $gposToCreate 'Tier2UserRightsAssignments') {$clawGpoList.Add([ClawGpo]::new("$($myClawDomainReadiness.Tier2UserRightsAssignments.GpoName)")) | out-null}
    } else {
        # we're doing the else here because the local admin splice GPO's must not create alongside the real tier 1 and tier 2 GPO's
        if (Use-GpoName $gposToCreate 'Tier1LocalAdminSplice') {$clawGpoList.Add([ClawGpo]::new("$($myClawDomainReadiness.Tier1LocalAdminSplice.GpoName)")) | out-null}
        if (Use-GpoName $gposToCreate 'Tier2LocalAdminSplice') {$clawGpoList.Add([ClawGpo]::new("$($myClawDomainReadiness.Tier2LocalAdminSplice.GpoName)")) | out-null}
    }

    foreach ($clawGpo in $clawGpoList) {
        $guid = $($myClawDomainReadiness.gpoReport["$($clawGpo.name)"].GpoGuid)
        if ($null -ne $guid) {
            if ($Force) {
                try {
                    if ($myDomain.RemoveGpo($null,$guid)) {
                        write-output "Removed GPO $($clawGpo.name) in domain $($myDomain.domainDn) with GUID $guid"
                        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Removed GPO $($clawGpo.name) in domain $($myDomain.domainDn) with GUID $guid" -logSev "Info" | out-null
                    } else {
                        throw
                    }
                } catch {
                    $clawGpoList.Find({param($x) $x.name -eq $clawGpo.name}).guid = $guid
                    write-warning "Force was specified but removal failed for GPO $($clawGpoToCreate.name)"
                    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Force was specified but removal failed for GPO $($clawGpoToCreate.name). $errorMessage" -logSev "Warn" | out-null
                }
            } else {
                $clawGpoList.Find({param($x) $x.name -eq $clawGpo.name}).guid = $guid
                write-output "GPO $($clawGpo.name) already exists in domain $($myDomain.domainDn) with GUID $guid"
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "GPO $($clawGpo.name) already exists in domain $($myDomain.domainDn) with GUID $guid" -logSev "Info" | out-null
            }
        } else {
            write-verbose "No GUID found for GPO $($clawGpo.name)"
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "No GUID found for GPO $($clawGpo.name)" -logSev "Info" | out-null
        }
        $guid = $null
    }
    # foreach gpo to create in the list of GPO's where the GUID is length 0 (e.g. not found)
    foreach ($clawGpoToCreate in $clawGpoList.FindAll({param($x) $x.guid.length -eq 0})) #we are creating one or more GPO's
    {
        switch ($clawGpoToCreate.name)
        {
            # steps per gpo: create, capture guid, set path to hash table, build block, set to hash table, write to disk
            "$($myClawDomainReadiness.Tier0UserRightsAssignments.GpoName)"  {
                $createdGpo = New-ClawGpoToCreate -clawGpoToCreate $($clawGpoList.Find({param($x) $x.name -eq "$($myClawDomainReadiness.Tier0UserRightsAssignments.GpoName)"})) -myDomain $mydomain
                if ($createdGpo.created -eq $true) {
                    Set-ClawT0UserRightsGpo -clawGpoGroups $clawGpoGroups -createdGpo $createdGpo -myDomain $myDomain
                } else {
                    write-warning "$($clawGpoToCreate.name) reported creation failure so we're not setting content."
                    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "$($clawGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                }
                
            }

            "$($myClawDomainReadiness.Tier0RestrictedGroups.GpoName)" {
                $createdGpo = New-ClawGpoToCreate -clawGpoToCreate $($clawGpoList.Find({param($x) $x.name -eq "$($myClawDomainReadiness.Tier0RestrictedGroups.GpoName)"})) -myDomain $mydomain
                if ($createdGpo.created -eq $true) {
                    Set-ClawT0RestrictedGroupsGpo -clawGpoGroups $clawGpoGroups -createdGpo $createdGpo -myDomain $myDomain
                } else {
                    write-warning "$($clawGpoToCreate.name) reported creation failure so we're not setting content."
                    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "$($clawGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                }
            }   

            "$($myClawDomainReadiness.Tier0DomainControllers.GpoName)" {
                $createdGpo = New-ClawGpoToCreate -clawGpoToCreate $($clawGpoList.Find({param($x) $x.name -eq "$($myClawDomainReadiness.Tier0DomainControllers.GpoName)"})) -myDomain $mydomain
                if ($createdGpo.created -eq $true) {
                    Set-ClawT0DomainControllersGpo -clawGpoGroups $clawGpoGroups -createdGpo $createdGpo -myDomain $myDomain
                } else {
                    write-warning "$($clawGpoToCreate.name) reported creation failure so we're not setting content."
                    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "$($clawGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                }
                
            }     

            "$($myClawDomainReadiness.Tier0DomainBlock.GpoName)" {
                $createdGpo = New-ClawGpoToCreate -clawGpoToCreate $($clawGpoList.Find({param($x) $x.name -eq "$($myClawDomainReadiness.Tier0DomainBlock.GpoName)"})) -myDomain $mydomain
                if ($createdGpo.created -eq $true) {
                    Set-ClawT0DomainBlockGpo -clawGpoGroups $clawGpoGroups -createdGpo $createdGpo -myDomain $myDomain
                } else {
                    write-warning "$($clawGpoToCreate.name) reported creation failure so we're not setting content."
                    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "$($clawGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                }
            }

            "$($myClawDomainReadiness.Tier0ESXAdminsRestrictedGroup.GpoName)" {
                $createdGpo = New-ClawGpoToCreate -clawGpoToCreate $($clawGpoList.Find({param($x) $x.name -eq "$($myClawDomainReadiness.Tier0ESXAdminsRestrictedGroup.GpoName)"})) -myDomain $mydomain
                if ($createdGpo.created -eq $true) {
                    Set-ClawT0ESXAdminsRestrictedGroupGpo -clawGpoGroups $clawGpoGroups -createdGpo $createdGpo -myDomain $myDomain
                } else {
                    write-warning "$($clawGpoToCreate.name) reported creation failure so we're not setting content."
                    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "$($clawGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                }
            }

            "$($myClawDomainReadiness.Tier0BaselineAudit.GpoName)" {
                $createdGpo = New-ClawGpoToCreate -clawGpoToCreate $($clawGpoList.Find({param($x) $x.name -eq "$($myClawDomainReadiness.Tier0BaselineAudit.GpoName)"})) -myDomain $mydomain
                if ($createdGpo.created -eq $true) {
                    Set-ClawT0BaselineAuditGpo -clawGpoGroups $clawGpoGroups -createdGpo $createdGpo -myDomain $myDomain
                } else {
                    write-warning "$($clawGpoToCreate.name) reported creation failure so we're not setting content."
                    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "$($clawGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                }
            }

            "$($myClawDomainReadiness.Tier0DisallowDSRMLogin.GpoName)" {
                $createdGpo = New-ClawGpoToCreate -clawGpoToCreate $($clawGpoList.Find({param($x) $x.name -eq "$($myClawDomainReadiness.Tier0DisallowDSRMLogin.GpoName)"})) -myDomain $mydomain
                if ($createdGpo.created -eq $true) {
                    Set-ClawT0DisallowDSRMLoginGpo -clawGpoGroups $clawGpoGroups -createdGpo $createdGpo -myDomain $myDomain
                } else {
                    write-warning "$($clawGpoToCreate.name) reported creation failure so we're not setting content."
                    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "$($clawGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                }
            }

            "$($myClawDomainReadiness.Tier1LocalAdminSplice.GpoName)" {
                $createdGpo = New-ClawGpoToCreate -clawGpoToCreate $($clawGpoList.Find({param($x) $x.name -eq "$($myClawDomainReadiness.Tier1LocalAdminSplice.GpoName)"})) -myDomain $mydomain
                if ($createdGpo.created -eq $true) {
                    Set-ClawT1LocalAdminSpliceGpo -clawGpoGroups $clawGpoGroups -createdGpo $createdGpo -myDomain $myDomain
                } else {
                    write-warning "$($clawGpoToCreate.name) reported creation failure so we're not setting content."
                    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "$($clawGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                }
            }

            "$($myClawDomainReadiness.TierAllDisableSMB1.GpoName)" {
                $createdGpo = New-ClawGpoToCreate -clawGpoToCreate $($clawGpoList.Find({param($x) $x.name -eq "$($myClawDomainReadiness.TierAllDisableSMB1.GpoName)"})) -myDomain $mydomain
                if ($createdGpo.created -eq $true) {
                    Set-ClawTAllDisableSMB1Gpo -clawGpoGroups $clawGpoGroups -createdGpo $createdGpo -myDomain $myDomain
                } else {
                    write-warning "$($clawGpoToCreate.name) reported creation failure so we're not setting content."
                    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "$($clawGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                }
            }

            "$($myClawDomainReadiness.TierAllDisableWdigest.GpoName)" {
                $createdGpo = New-ClawGpoToCreate -clawGpoToCreate $($clawGpoList.Find({param($x) $x.name -eq "$($myClawDomainReadiness.TierAllDisableWdigest.GpoName)"})) -myDomain $mydomain
                if ($createdGpo.created -eq $true) {
                    Set-TAllDisableWdigestGpo -clawGpoGroups $clawGpoGroups -createdGpo $createdGpo -myDomain $myDomain
                } else {
                    write-warning "$($clawGpoToCreate.name) reported creation failure so we're not setting content."
                    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "$($clawGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                }
            }        

            "$($myClawDomainReadiness.Tier1UserRightsAssignments.GpoName)"  {
                $createdGpo = New-ClawGpoToCreate -clawGpoToCreate $($clawGpoList.Find({param($x) $x.name -eq "$($myClawDomainReadiness.Tier1UserRightsAssignments.GpoName)"})) -myDomain $mydomain
                if ($createdGpo.created -eq $true) {
                    Set-ClawT1UserRightsGpo -clawGpoGroups $clawGpoGroups -createdGpo $createdGpo -myDomain $myDomain
                } else {
                    write-warning "$($clawGpoToCreate.name) reported creation failure so we're not setting content."
                    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "$($clawGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                }
                
            }

            "$($myClawDomainReadiness.Tier1RestrictedGroups.GpoName)" {
                $createdGpo = New-ClawGpoToCreate -clawGpoToCreate $($clawGpoList.Find({param($x) $x.name -eq "$($myClawDomainReadiness.Tier1RestrictedGroups.GpoName)"})) -myDomain $mydomain
                if ($createdGpo.created -eq $true) {
                    Set-ClawT1RestrictedGroupsGpo -clawGpoGroups $clawGpoGroups -createdGpo $createdGpo -myDomain $myDomain
                } else {
                    write-warning "$($clawGpoToCreate.name) reported creation failure so we're not setting content."
                    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "$($clawGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                }
            }

            "$($myClawDomainReadiness.Tier2LocalAdminSplice.GpoName)" {
                $createdGpo = New-ClawGpoToCreate -clawGpoToCreate $($clawGpoList.Find({param($x) $x.name -eq "$($myClawDomainReadiness.Tier2LocalAdminSplice.GpoName)"})) -myDomain $mydomain
                if ($createdGpo.created -eq $true) {
                    Set-ClawT2LocalAdminSpliceGpo -clawGpoGroups $clawGpoGroups -createdGpo $createdGpo -myDomain $myDomain
                } else {
                    write-warning "$($clawGpoToCreate.name) reported creation failure so we're not setting content."
                    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "$($clawGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                }
            }

            "$($myClawDomainReadiness.Tier2UserRightsAssignments.GpoName)"  {
                $createdGpo = New-ClawGpoToCreate -clawGpoToCreate $($clawGpoList.Find({param($x) $x.name -eq "$($myClawDomainReadiness.Tier2UserRightsAssignments.GpoName)"})) -myDomain $mydomain
                if ($createdGpo.created -eq $true) {
                    Set-ClawT2UserRightsGpo -clawGpoGroups $clawGpoGroups -createdGpo $createdGpo -myDomain $myDomain
                } else {
                    write-warning "$($clawGpoToCreate.name) reported creation failure so we're not setting content."
                    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "$($clawGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                }
                
            }

            "$($myClawDomainReadiness.Tier2RestrictedGroups.GpoName)" {
                $createdGpo = New-ClawGpoToCreate -clawGpoToCreate $($clawGpoList.Find({param($x) $x.name -eq "$($myClawDomainReadiness.Tier2RestrictedGroups.GpoName)"})) -myDomain $mydomain
                if ($createdGpo.created -eq $true) {
                    Set-ClawT2RestrictedGroupsGpo -clawGpoGroups $clawGpoGroups -createdGpo $createdGpo -myDomain $myDomain
                } else {
                    write-warning "$($clawGpoToCreate.name) reported creation failure so we're not setting content."
                    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "$($clawGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                }
            }

            "$($myClawDomainReadiness.MachineAccountPassword.GpoName)" {
                $createdGpo = New-ClawGpoToCreate -clawGpoToCreate $($clawGpoList.Find({param($x) $x.name -eq "$($myClawDomainReadiness.MachineAccountPassword.GpoName)"})) -myDomain $mydomain
                if ($createdGpo.created -eq $true) {
                    Set-ClawMachineAccountPasswordGpo -clawGpoGroups $clawGpoGroups -createdGpo $createdGpo -myDomain $myDomain
                } else {
                    write-warning "$($clawGpoToCreate.name) reported creation failure so we're not setting content."
                    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "$($clawGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                }
            }
        }
    }
    
}
function New-ClawGpoToCreate
{
    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$true)]
        $clawGpoToCreate,
        [Parameter(Mandatory=$true)]
        $myDomain
    )
    # create block is constant
    try {
        $create = $myDomain.NewGpo("$($clawGpoToCreate.name)",1)
        if (!($create)) {
            throw 
        }
    } catch {
        write-error "Attempt to create $($clawGpoToCreate.name) failed because returned GUID was null"
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Attempt to create $($clawGpoToCreate.name) failed because returned GUID was null" -logSev "Error" | out-null
    }
    if ($create) {
        $clawGpoToCreate.guid = $create
        $clawGpoToCreate.created = $true
        write-host "Created $($clawGpoToCreate.name) in domain $($myDomain.domainFqdn)"
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Created $($clawGpoToCreate.name) in domain $($myDomain.domainFqdn)" -logSev "Info" | out-null
    } else {
        write-error "Failed to create $($clawGpoToCreate.name) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to create $($clawGpoToCreate.name) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    start-sleep 3 #for repl

    #return the modified object
    return $clawGpoToCreate
}
function New-ClawGroup
{

    ##########################################################################################################
    <#
    .SYNOPSIS
        Creates CLAW groups.
    
    .DESCRIPTION
        Creates CLAW groups

    .EXAMPLE
        New-ClawGroup -Domain halo.net

        Creates the required groups's for CLAW

    .OUTPUTS
        Verbose output to host.
        General log written to '$env:temp\claw.log'

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

    ###################################
    ## Function Options and Parameters
    ###################################

    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$false)]
        [string]$IDOUName="SITH",
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )
    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Entering function New-ClawGroup" -logSev "Info" | out-null
    write-verbose "Entering function New-ClawGroup"
    #Get domain object
    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain

    #check here for exist of the OU structure and if it's not there then you need to call create ou
    if (!(Get-ClawOu -IDOUName $IDOUName -myDomain $myDomain)) {
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Missing OU's detected. Running New-ClawOu" -logSev "Info" | out-null
        New-ClawOu -IDOUName $IDOUName -myDomain $myDomain
    }
    # where is SITH
    $IDOUNamePath = Get-IDOUNamePath -IDOUName $IDOUName -myDomain $mydomain

    $clawGroupList = [System.Collections.Generic.List[ClawGroup]]::new()
    $clawGroupList.Add([ClawGroup]::new("Tier 0 Operators","Members of this group operate Tier 0 and will be local admins on Tier 0 servers","OU=T0-Operators,OU=T0-Groups,OU=Tier 0,$IDOUNamePath")) | out-null
    $clawGroupList.Add([ClawGroup]::new("Tier 0 Service Accounts","Members of this group can run services on Tier 0","OU=T0-Groups,OU=Tier 0,$IDOUNamePath")) | out-null
    $clawGroupList.Add([ClawGroup]::new("Tier 0 Computers","Members of this group are Tier 0 computers and will be exempted from the default DA block","OU=T0-Groups,OU=Tier 0,$IDOUNamePath")) | out-null
    $clawGroupList.Add([ClawGroup]::new("CLAW Domain Join","Members of this group are delegated to join computers to the Staging OU","OU=Groups,OU=CLAW,$($myDomain.domainDn)")) | out-null
    $clawGroupList.Add([ClawGroup]::new("ESX Admins","Members of this group are automatically granted root access on an ESX server that is joined to the domain. THIS GROUP SHOULD BE EMPTY!","OU=Groups,OU=CLAW,$($myDomain.domainDn)")) | out-null
    $clawGroupList.Add([ClawGroup]::new("Tier 1 Operators","Members of this group operate Tier 1 and will be local admins on Tier 1 servers","OU=T1-Operators,OU=T1-Groups,OU=Tier 1,$IDOUNamePath")) | out-null
    $clawGroupList.Add([ClawGroup]::new("Tier 2 Operators","Members of this group operate Tier 2 and will be local admins on Tier 2 devices","OU=T2-Operators,OU=T2-Groups,OU=Tier 2,$IDOUNamePath")) | out-null
    foreach ($clawGroup in $clawGroupList) {
        #Check if group exists
        $group = $null
        try {
            $group = $myDomain.GetObjectByName($clawGroup.name,"domain",$false)
        }
        catch {}
        if (!($group)) {
            try {
                $newGroupCreate = $myDomain.NewAdGroup($clawGroup.dn,$clawGroup.name,$clawGroup.description,"global")
                if (!($newGroupCreate)) {
                    throw
                }
                write-output "Created group: $($clawGroup.name) at $($clawGroup.dn)"
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Created group: $($clawGroup.name) at $($clawGroup.dn)" -logSev "Info" | out-null
                start-sleep 1
            }
            catch {
                write-error "Failed to create group: $($clawGroup.name) at $($clawGroup.dn). See $env:temp\claw.log for details."
                $errorMessage = $_.Exception
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to create group: $($clawGroup.name) at $($clawGroup.dn). $errorMessage" -logSev "Error" | out-null
            }
        } else {
            if ($clawGroup.name -eq "ESX Admins") {
                write-warning "ESX Admins group already exists, running access report"
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "ESX Admins group already exists, running access report" -logSev "Warn" | out-null
                Get-AdPrivRoleReport -object "ESX Admins" -recurse -myDomain $myDomain
            }
        }
    }

    start-sleep 3
    try {
        $daGroupName = $myDomain.privilegedGroupNames["Domain Admins"]
        if (!($myDomain.IsObjectInGroup($daGroupName,"domain","Tier 0 Operators","domain"))) {
            $addCheck = $myDomain.AddObjectToGroup($daGroupName,"domain","Tier 0 Operators","domain")
            if (!($addCheck)) {
                throw
            }
            write-output "Added $daGroupName to Tier 0 Operators."
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Added $daGroupName to Tier 0 Operators." -logSev "Info" | out-null
        } else {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "$daGroupName already a member of Tier 0 Operators." -logSev "Info" | out-null
        }
    }
    catch {
        Write-error "Failed to add $daGroupName to Tier 0 Operators. See $env:temp\claw.log for details."
        $errorMessage = $_.Exception
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to add $daGroupName to Tier 0 Operators. $errorMessage" -logSev "Error" | out-null
    }
    # Add domain controllers to Tier 0 computers
    start-sleep 3
    try {
        $dcGroupName = $myDomain.GetObjectBySid("$($myDomain.domainsid)-516","domain").properties.samaccountname
        if (!($myDomain.IsObjectInGroup($dcGroupName,"domain","Tier 0 Computers","domain"))) {
            $addCheck = $myDomain.AddObjectToGroup($dcGroupName,"domain","Tier 0 Computers","domain")
            if (!($addCheck)) {
                throw
            }
            write-output "Added $dcGroupName to Tier 0 Computers."
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Added $dcGroupName to Tier 0 Computers." -logSev "Info" | out-null
        } else {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "$dcGroupName already a member of Tier 0 Computers." -logSev "Info" | out-null
        }
    }
    catch {
        Write-error "Failed to add $dcGroupName to Tier 0 Computers. See $env:temp\claw.log for details."
        $errorMessage = $_.Exception
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to add $dcGroupName to Tier 0 Computers. $errorMessage" -logSev "Error" | out-null
    }
    $loopcount=0
    try {
        if (!("$($myDomain.domainNetbiosName)\CLAW Domain Join" -in $($myDomain.GetAdAcl("OU=Staging,$IDOUNamePath","domain"))).identityreference) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Adding ACLs for $($myDomain.domainNetbiosName)\CLAW Domain Join to OU=Staging,$IDOUNamePath" -logSev "Info" | out-null
            $keepLooping = $true
            while ($keepLooping -and $loopcount -lt 4) {
                $x=$null
                try {
                    $x = $myDomain.GetObjectByName("CLAW Domain Join","domain",$false)
                    if ($x) {
                        $keepLooping = $false
                    }
                } catch {
                    write-warning "Possible replication lag detected for `'$($myDomain.domainNetbiosName)\CLAW Domain Join`' creation. Sleeping for 10 more seconds..."
                    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Possible replication lag detected for `'$($myDomain.domainNetbiosName)\CLAW Domain Join`' creation. Sleeping for 10 more seconds..." -logSev "Warn" | out-null
                    start-sleep 10
                }
                $loopcount++
            }
            if ($x) {
                Set-ClawDomainJoinDelegation -IDOUName $IDOUName -delegateTo "CLAW Domain Join" -domain $domain -myDomain $myDomain
            } else {
                write-warning "$($myDomain.domainNetbiosName)\CLAW Domain Join not found! Skipping delegation configuration, to run manually please confirm group creation and run command Set-ClawDomainJoinDelegation -domain $($myDomain.domainFqdn)"
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "$($myDomain.domainNetbiosName)\CLAW Domain Join not found! Skipping delegation configuration, to run manually please confirm group creation and run command Set-ClawDomainJoinDelegation -domain $($myDomain.domainFqdn)" -logSev "Warn" | out-null
            }
        }
        
    } 
    catch {
        $errorMessage = $_.Exception
        write-error "Failed to gather information for OU OU=Staging,$IDOUNamePath"
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to gather information for OU OU=Staging,$IDOUNamePath. $errorMessage" -logSev "Error" | out-null
    }
}
function New-ClawOu
{

    ##########################################################################################################
    <#
    .SYNOPSIS
        Creates CLAW OU structure.
    
    .DESCRIPTION
        Creates CLAW OU structure

    .EXAMPLE
        New-ClawOu

        Creates the required OU's for CLAW

    .OUTPUTS
        Verbose output to host.
        General log written to '$env:temp\claw.log'

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

    ###################################
    ## Function Options and Parameters
    ###################################

    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$false)]
        [string]$IDOUName="SITH",
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )
    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Entering function New-ClawOu" -logSev "Info" | out-null
    write-verbose "Entering function New-ClawOu"
    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
    # where is SITH
    $IDOUNamePath = Get-IDOUNamePath -IDOUName $IDOUName -myDomain $mydomain
    $clawRootOu = "OU=CLAW,$($myDomain.domainDn)"
    # build OU tree. start with what always gets created
    $clawOus = Get-ClawOuManifest -IDOUNamePath $IDOUNamePath -myDomain $myDomain
   
    $proceedWithOuTreeCreation = $false
    # start with clawRootOu
    try {
        $clawRootExists = $null
        try {
            $clawRootExists = $myDomain.GetObjectByDn($clawRootOu,"domain")
        } catch {}
        
        if (!($clawRootExists)) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Claw OU root does not exist at: $clawRootOu" -logSev "Info" | out-null
            try {
                $obj=$myDomain.NewAdOu($($clawRootOu.Substring($clawRootOu.IndexOf(",")+1)),($clawRootOu -split ",")[0].Substring(3),$null)
                start-sleep 1
                # make sure it created
                $keepLooping = $true
                $loopcount = 0
                while ($keepLooping -and $loopcount -lt 4) {
                    try {
                        $clawRootExists = $myDomain.GetObjectByDn($clawRootOu,"domain")
                    } catch {}
                    
                    if ($clawRootExists) {
                        write-output "Created OU: $clawRootOu"
                        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Created OU: $clawRootOu" -logSev "Info" | out-null

                        $proceedWithOuTreeCreation = $true
                        $keepLooping = $false
                    } else {
                        write-warning "Possible replication lag detected for `'$clawRootOu`' creation. Sleeping for 10 more seconds..."
                        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Possible replication lag detected for `'$clawRootOu`' creation. Sleeping for 10 more seconds..." -logSev "Warn" | out-null
                        start-sleep 10
                    }
                    $loopcount++
                }
                if (!($obj)) {
                    throw
                }
            } catch {
                write-error "Failed to create CLAW root OU $clawRootOu. See $env:temp\claw.log for details. No further OU's can be created!"
                $errorMessage = $_.Exception
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to create CLAW root OU $clawRootOu. $errorMessage" -logSev "Error" | out-null
                throw
            }
        } else {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Claw OU root does exist at: $clawRootOu. Safe to proceed with OU tree creation." -logSev "Info" | out-null
            $proceedWithOuTreeCreation = $true
        }
    } catch {
        $clawRootExists = $null
        $proceedWithOuTreeCreation = $false
        write-warning "Root OU failed to create at $clawRootOu. Further actions may not succeed."
        start-sleep 10
    }
    
    # create ou's only if root created
    if ($proceedWithOuTreeCreation) {
        foreach ($ou in $clawOus) {
            # idempotency test
            $ouCheck=""
            try {
                $ouCheck = $myDomain.GetObjectByDn($ou,"domain")
            } catch {}
            if (!($ouCheck)) {
                try {
                    $obj=$myDomain.NewAdOu($($ou.Substring($ou.IndexOf(",")+1)),($ou -split ",")[0].Substring(3),$null)
                    if (!($obj)) {
                        throw
                    }
                    write-output "Created OU: $ou"
                    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Created OU: $ou" -logSev "Info" | out-null
                    start-sleep 1
                }
                catch {
                    $errorMessage = $_.Exception
                    write-error "Failed to create OU: $ou. See $env:temp\claw.log for details."
                    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to create OU: $ou. $errorMessage" -logSev "Error" | out-null
                }
            } else {
                write-output "OU already exists: $ou"
            }
        }
        # tag the IDOUName
        try {
            if ($myDomain.SetObjectByDn("$IDOUNamePath","adminDescription","SITH-MIRCAT","domain")) {
                write-output "Tagged $IDOUNamePath with adminDescription SITH-MIRCAT"
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Tagged $IDOUNamePath with adminDescription SITH-MIRCAT" -logSev "Info" | out-null
            } else {
                throw
            }
        } catch {
            write-error "Failed to tag $IDOUNamePath with adminDescription SITH-MIRCAT. See $env:temp\claw.log for details."
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to tag $IDOUNamePath with adminDescription SITH-MIRCAT. $errorMessage" -logSev "Error" | out-null
        }
        
        # sleep for repl
        start-sleep 1
        # set block inheritance
        $blockOus = @()
        $blockOus += "OU=Computer Quarantine,OU=CLAW,$($myDomain.domainDn)"
        $blockOus += "$IDOUNamePath"
        $blockOus += "OU=Staging,$IDOUNamePath"
        $blockOus += "OU=Staging,OU=Tier 1 Servers,OU=CLAW,$($myDomain.domainDn)"
        $blockOus += "OU=Staging,OU=T2-Devices,OU=CLAW,$($myDomain.domainDn)"
        foreach ($blockOu in $blockOus) {
            $block = $null
            try {
                $block = $myDomain.SetGpInheritance($blockOu,1)
                write-output "Blocked inheritance on ou: $blockOu"
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Blocked inheritance on ou: $blockOu" -logSev "Info" | out-null
            }
            catch {
                $errorMessage = $_.Exception
                write-error "Failed to block inheritance on OU: $blockOu. See $env:temp\claw.log for details."
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to block inheritance on OU: $blockOu. $errorMessage" -logSev "Error" | out-null
            }
        }
    }
    
}
Function New-EntraIRCAPolicies
{
  [CmdletBinding()]
  param (
    [Parameter(Mandatory = $true)]
    [string]
    $tenantid,

    [Parameter()]
    [string]
    $apiversion = "v1.0",

    # BreakGlass account UPN or ID
    [Parameter(Mandatory = $true)]
    [Array]
    $breakglassAccounts,

    # Named location that will be marked as trusted for registration of security info
    # Provide CIDR notation
    [Parameter(Mandatory = $false)]
    [array]
    $namedLocations,

    # The prefix for the policy name. This is used to identify the policies that are created by this script.
    [Parameter(mandatory = $false)]
    [string]
    $prefix = "[IR]",

    # Template names to deploy
    [Parameter(Mandatory = $false)]
    [array]
    $templateToDeploy = @("Block legacy authentication",
                          "Require multifactor authentication for admins",
                          "Require multifactor authentication for Azure management",
                          "Securing security info registration",
                          "Require password change for high-risk users",
                          "Require multifactor authentication for risky sign-ins"),

    [Parameter()]
    [switch]
    $enforceEnable
  )

  <#
  $scopes = "Policy.Read.All", 
            "Policy.ReadWrite.ConditionalAccess"
  #>

  #$currentUser = Invoke-GraphRequest -Uri "https://graph.microsoft.com/$apiversion/me" -Method GET

  $breakglassAccountsIdList = @()

  #Validate exempted users existance
  If ($breakglassAccounts.Count -gt 0)
  {
    foreach ($exemptedUser in $breakglassAccounts)
    {
      try {
        $user = Get-EntraObject -objectType "user" -objectId $exemptedUser
        $breakglassAccountsIdList += $user.id
      }
      catch {
        Write-Error -Message "User $exemptedUser does not exist in the tenant."
        exit
      }
    }
  } Else {
    Write-Error -Message "No users excluded."
    exit
  }

  If ($namedLocations.Count -gt 0)
  {
    Foreach ($cidr in $namedLocations)
    {
      # Validate if CIDR is in correct format
      $cidrregex = "^(?:[0-9]{1,3}\.){3}[0-9]{1,3}/[0-9]{1,2}$"
      if ($cidr -notmatch $cidrregex)
      {
        Write-Error -Message "Invalid CIDR in named locations list."
        exit
      }
    }
  } Else {
    Write-Host -Message "No named locations provided." -ForegroundColor Cyan
  }

  <# 

    Template IDs seem to be the same for all tenants. 
    If the dynamic approach with graph queries is to tedious we can switch to static values.

    $CAPolicyTemplates = 
    @{
      "Require multifactor authentication for admins"                                                   = "c7503427-338e-4c5e-902d-abe252abfb43"
      "Securing security info registration"                                                             = "b8bda7f8-6584-4446-bce9-d871480e53fa"
      "Block legacy authentication"                                                                     = "0b2282f9-2862-4178-88b5-d79340b36cb8"
      "Require multifactor authentication for all users"                                                = "a3d0a415-b068-4326-9251-f9cdf9feeb64"
      "Require multifactor authentication for guest access"                                             = "a4072ac0-722b-4991-981b-7f9755daef14"
      "Require multifactor authentication for Azure management"                                         = "d8c51a9a-e6b1-454d-86af-554e7872e2c1"
      "Require multifactor authentication for risky sign-ins"                                           = "6b619f55-792e-45dc-9711-d83ec9d7ae90"
      "Require password change for high-risk users"                                                     = "634b6de7-c38d-4357-a2c7-3842706eedd7"
      "Require compliant or hybrid Azure AD joined device for admins"                                   = "c26a510a-3b8b-4023-8c44-d4f4c854e9f9"
      "Block access for unknown or unsupported device platform"                                         = "4e39a309-931e-4cb1-a371-e2beea168002"
      "No persistent browser session"                                                                   = "62e51ccc-c9c3-4554-ac70-066172c81007"
      "Require approved client apps or app protection policies"                                         = "6acdf4c3-6815-485c-a57d-2c349d517ba0"
      "Require compliant or hybrid Azure AD joined device or multifactor authentication for all users"  = "927c884e-7888-4e81-abc4-bd56ded28985"
      "Use application enforced restrictions for O365 apps"                                             = "81fd2072-4876-42b6-8157-c6000693046b"
      "Require phishing-resistant multifactor authentication for admins"                                = "76c03f19-ea37-4656-a772-a183b4ddb81d"
      "Require multifactor authentication for Microsoft admin portals"                                  = "6364131e-bc4a-47c4-a20b-33492d1fff6c"
      "Block access to Office365 apps for users with insider risk"                                      = "16aaa400-bfdf-4756-a420-ad2245d4cde8"
    }

  #>

  # Need to override the api version to beta.
  # https://learn.microsoft.com/en-us/entra/identity/role-based-access-control/privileged-roles-permissions?tabs=ms-graph

  $roleDefinitions = [entraRoleManagement]::new(@{apiVersion = 'beta'; serviceName = 'directory' ; componentName = 'roleDefinitions'})
  $privilegedRoleDefinitions = $roleDefinitions.get(@{filter="isPrivileged eq true"}).value
  $CAPolicyTemplates = [entraCAManagement]::new(@{componentName = 'templates'; apiVersion = 'v1.0'}).get(@{}).value
  $CAPolicyManagement = [entraCAManagement]::new(@{componentName = 'policies'; apiVersion = 'v1.0'})
  $existingpolicies = $CAPolicyManagement.get(@{}).value

  Foreach ($templateName in $templateToDeploy)
  {
    $PolicyDefinition = $CAPolicyTemplates | Where-Object {$_.Name -ieq $templateName}
    $PolicyDefinition.details | add-member -type NoteProperty -Name displayName -Value ($prefix, $templateName -join " ")
    $PolicyDefinition.details.conditions.users.excludeUsers = $breakglassAccountsIdList

    if ($enforceEnable)
    {
      $PolicyDefinition.details | add-member -MemberType NoteProperty -Name state -Value "enabled"
    } else {
      $PolicyDefinition.details | add-member -MemberType NoteProperty -Name state -Value "enabledForReportingButNotEnforced"
    }

    # The admin roles defined in the templates don't include all roles that are marked as privileged.
    # This will add all roles marked as privileged to the scope of the policy of the template is scoped to admin roles.
    If($PolicyDefinition.details.conditions.users.includeroles.count -gt 1) 
    {
      $PolicyDefinition.details.conditions.users.includeroles = $privilegedRoleDefinitions.id
    }

    # We dont want to scope the sign in frequency policy to all users but only to admins.
    If ($PolicyDefinition.id -eq "62e51ccc-c9c3-4554-ac70-066172c81007")
    {
      $PolicyDefinition.details.conditions.users.includeroles = $privilegedRoleDefinitions.id
    }

    If ($existingpolicies.displayName -notcontains $($PolicyDefinition.details.displayName))
    {
      $CAPolicyManagement.Create(@{postBody = $($PolicyDefinition.details)})
    } Else {
      $polstate = ($existingpolicies[$existingpolicies.displayName.indexof($($PolicyDefinition.details.displayName))]).state
      If ($enforceEnable -and $polstate -ne "enabled")
      {
        Write-Host "Policy $($PolicyDefinition.details.displayName) already exists nut not enabled. Enabling." -ForegroundColor Green
        $polid = ($existingpolicies[$existingpolicies.displayName.indexof($($PolicyDefinition.details.displayName))]).id
        $CAPolicyManagement.Update(@{postBody = @{"state" = $($PolicyDefinition.details.state)}; objectId = $polid})
      } Else {
        Write-Host "Policy $($PolicyDefinition.details.displayName) already exists. Skipping." -ForegroundColor Gray
      }
    }
  }
}
function New-EntraObject
{
  # Example usage:
  # New-EntraObject -objectType user -postBody @{accountEnabled = $true; displayName = "Mircat"}

  [CmdletBinding()]
  param
  (
    [Parameter(Mandatory = $true)]
    #[ValidateSet([graphObjectTypeValidator])] # This is not working in powershell 5.0
    [ValidateScript({
      $validValues = [graphObjectTypePermissions]::new().getAll().Keys
      if ($_ -in $validValues) {
          $true
      } else {
          throw "Invalid value '$_'. Valid values are: $($validValues -join ', ')"
      }
    })]
    [string] $objectType,
  
    [Parameter(Mandatory = $true)]
    [hashtable] $postBody
  )

  $graphCollection = [GraphAL]::new(@{objectType = $objectType})
  $graphCollection.Create(@{postBody = $postBody})
}
function New-GpoHeader 
{
    $gpoFileHeader = '[Unicode]{0}' -f [environment]::NewLine
    $gpoFileHeader += 'Unicode=yes{0}' -f [environment]::NewLine
    $gpoFileHeader += '[Version]{0}' -f [environment]::NewLine
    $gpoFileHeader += 'signature="$CHICAGO$"{0}' -f [environment]::NewLine
    $gpoFileHeader += 'Revision=1'

    return $gpoFileHeader
}
function New-MDIDeploy {
    [CmdletBinding()]
    Param(
        [parameter(Mandatory=$true)]
        [ValidateSet("Online","Offline")][string]$Mode,
        [Parameter(Mandatory=$true)]
        [string]$Identity,
        [parameter(Mandatory = $true)]
        [ValidateSet('Domain', 'Forest')]
        [string]$IdentityLocation,
        [Parameter(Mandatory=$false)]
        [uri] $Proxy,
        [parameter(Mandatory = $true)]
        [ValidateSet('Kusto', 'Local')]
        [string]$LogLocation,
        [Parameter(Mandatory=$false)]
        [ValidateSet("Commercial","GCC","GCC-H","DOD")]
        [string]$CloudType,
        [Parameter(Mandatory=$false)]
        [switch]$IgnoreNPCAP,
        [Parameter(Mandatory=$false)]
        [string]$Path="$env:temp\MSS\MDISetup",
        [parameter(Mandatory=$false)]
        [switch]$Force
    )
    DynamicParam {
        $paramDictionary = New-Object -Type System.Management.Automation.RuntimeDefinedParameterDictionary
        # Define parameter attributes
        $paramAttributes = New-Object -Type System.Management.Automation.ParameterAttribute
        $paramAttributes.Mandatory = $true
        $paramAttributesCollect = New-Object -Type System.Collections.ObjectModel.Collection[System.Attribute]
        $paramAttributesCollect.Add($paramAttributes)
        $dynParamAccessKey = New-Object -Type System.Management.Automation.RuntimeDefinedParameter("AccessKey", [string], $paramAttributesCollect)
        if ($Mode -eq "Offline") { 
            $paramDictionary.Add("AccessKey", $dynParamAccessKey)
        }
        $paramAttributes = New-Object -Type System.Management.Automation.ParameterAttribute
        $paramAttributes.Mandatory = $true
        $paramAttributesCollect = New-Object -Type System.Collections.ObjectModel.Collection[System.Attribute]
        $paramAttributesCollect.Add($paramAttributes)
        $dynParamStorageAccountName = New-Object -Type System.Management.Automation.RuntimeDefinedParameter("StorageAccountName", [string], $paramAttributesCollect)
        $paramAttributes = New-Object -Type System.Management.Automation.ParameterAttribute
        $paramAttributes.Mandatory = $true
        $paramAttributesCollect = New-Object -Type System.Collections.ObjectModel.Collection[System.Attribute]
        $paramAttributesCollect.Add($paramAttributes)
        $dynParamSASToken = New-Object -Type System.Management.Automation.RuntimeDefinedParameter("SASToken", [string], $paramAttributesCollect)
        if ($LogLocation -eq 'Kusto') {
            $paramDictionary.Add("StorageAccountName", $dynParamStorageAccountName)
            $paramDictionary.Add("SASToken", $dynParamSASToken)
        }
        return $paramDictionary
    }
    begin {
        foreach ($key in $PSBoundParameters.Keys) {
      	    if ($MyInvocation.MyCommand.Parameters.$key.isDynamic) {
                Set-Variable -Name $key -Value $PSBoundParameters.$key
            }
    	}
    }
    process {
        if ($Proxy) {
            if ( -not ($Proxy.IsAbsoluteUri -and $Proxy.Port -ne -1 )) {
                throw 'Proxy format should be ''http://<host>:<port>'''
            }
        }
        if ($StorageAccountName) {
            if (-not ($StorageAccountName.StartsWith('ircstc'))) {
                throw "StorageAccountName must start with ircstc"
            }
        }
        if ($SASToken) {
            if (-not ($SASToken -match '^\?sv\=\d{4}(-\d{2}){2}(&sr=c|&si=defaultinbound){2}&sig=')) {
                throw "Invalid SASToken format"
            }
        }
        if ([string]::IsNullOrEmpty($CloudType)) {
            $CloudType = "Commercial"
        }
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Entering function New-MDIDeploy" -logSev "Info" | out-null
        write-output "Entering function New-MDIDeploy"
        $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
        if (($myDomain.forestSid -ne $myDomain.domainSid) -and ($IdentityLocation -eq 'Forest')) {
            $myDomain = Initialize-MyDomain -domain $myDomain.forestFqdn
        }
        if ([string]::IsNullOrEmpty($myDomain.writableSysvolPath)) {
            write-error "Failed to discover SYSVOL. See $env:temp\mircatmdi.log for details. Critical stop!"
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to discover SYSVOL $($_.Exception)" -logSev "Error" | out-null
            throw
        }
        try {
            $null = new-item -type Directory -path $path -force
        } catch {}
        write-output "Setting Domain Readiness"
        Set-MDIDomainReadiness -Identity $Identity -IdentityLocation $IdentityLocation -Force:$Force
        write-output "Creating deploy script"
        $assetsScriptPath = '{0}\{1}\{2}' -f $((get-module -name MIRCAT) | split-path),'assets','Invoke-MDISetup.ps1'
        if (-not (Test-Path $Path)) { 
            $null = New-Item -type Directory -path $Path -force
        }
        if (Test-Path $assetsScriptPath) {
            Copy-Item -Path $assetsScriptPath -Destination $Path
        } else {
            throw "Unable to find Asset: $assetsScriptPath"
        } 
        write-output "Creating deploy GPO"
        New-MDIGpo -gposToCreate DeployGpo -myDomain $myDomain -Force:$Force
        if ($Mode -eq "Online") {
            write-output "Connecting to portal"
            Use-MdiGraph
            if ([bool](Get-mgcontext)) {
                try {
                    write-output "Getting access key"
                    $AccessKey = (Get-MgBetaSecurityIdentitySensorDeploymentAccessKey).DeploymentAccessKey
                } catch {
                    start-sleep 5
                }
                if (!($AccessKey)) {
                    $AccessKey = (Get-MgBetaSecurityIdentitySensorDeploymentAccessKey).DeploymentAccessKey
                }
                try {
                    write-output "Getting download URL"
                    $dlurl = (Get-MgBetaSecurityIdentitySensorDeploymentPackageUri).downloadurl
                } catch {
                    start-sleep 5
                }
                if (!($dlUrl)) {
                    $dlurl = (Get-MgBetaSecurityIdentitySensorDeploymentPackageUri).downloadurl
                }
                if ($dlurl) {
                    $invokeWebReqParams = @{
                        method = "GET"
                        URI = $dlurl
                        OutFile = "$Path\azureatp.zip"
                    }; if (-not ([string]::IsNullOrEmpty($Proxy))) {$invokeWebReqParams.Add("Proxy","$Proxy")}
                    write-output "Getting ATP sensor installer"
                    $ProgressPreference= 'SilentlyContinue'
                    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
                    Invoke-WebRequest @invokeWebReqParams
                    Expand-Archive -Path "$Path\azureatp.zip" -DestinationPath "$Path" -force
                    write-output "Expanded ATP sensor installer"
                    Remove-item "$Path\azureatp.zip"
                } else {
                    write-warning "Unable to download ATP sensor package. Please download manually from security.microsoft.com"
                }
            } else {
                write-warning "Unable to get current MS Graph context. Try to connect again or run in Offline mode"
            }
        }
        write-output "Creating config file"
        $adObject = $mydomain.GetObjectByFilter("|(samaccountname=$Identity)(samaccountname=$($Identity)$)","domain")
        if ([string]::IsNullOrEmpty($AccessKey)) {
            $AccessKey = read-host -Prompt "AccessKey not found. Please enter the AccessKey from the portal to proceed"
        }
        $mdiConfigFileParams = @{
            Path = $Path
            AccessKey = $AccessKey
            Identity = $($adObject.Properties["samaccountname"])
            CloudType = $CloudType
            IgnoreNPCAP = $IgnoreNPCAP
        }
        if (-not ([string]::IsNullOrEmpty($Proxy))) {$mdiConfigFileParams.Add("Proxy","$Proxy")} 
        if (-not ([string]::IsNullOrEmpty($SASToken))) {$mdiConfigFileParams.Add("SASToken",$SASToken)}
        if (-not ([string]::IsNullOrEmpty($StorageAccountName))) {$mdiConfigFileParams.Add("StorageAccountName",$StorageAccountName)}
        Set-MDIConfigFile @mdiConfigFileParams
        $mdiRootCertParams = @{
            SkipInstall = $true
            Download = $true
            Path = $Path
        }
        if (-not ([string]::IsNullOrEmpty($Proxy))) { 
            $mdiRootCertParams.Add("Proxy",$Proxy)
        }
        $null = Set-MdiRootCert @mdiRootCertParams
        write-output "Moving install files to SYSVOL"
        $null = New-Item -type Directory -path "$($myDomain.writableSysvolPath)\scripts\MSS" -force
        Move-Item -Path "$Path" -Destination "$($myDomain.writableSysvolPath)\scripts\MSS" -force
    }
}
function New-MDIDSA {
    [CmdletBinding(DefaultParameterSetName = "gmsaAccount")]
    Param(
        [parameter(Mandatory = $true, ParameterSetName = "gmsaAccount")]
        [parameter(Mandatory = $true, ParameterSetName = "standardAccount")]
        [ValidateLength(1, 15)]
        [string]$Identity,
        [parameter(Mandatory = $true, ParameterSetName = "gmsaAccount")]
        [parameter(Mandatory = $true, ParameterSetName = "standardAccount")]
        [ValidateSet('Domain', 'Forest')]
        [string]$IdentityLocation,
        [parameter(Mandatory = $true, ParameterSetName = "gmsaAccount")]
        [ValidateLength(1, 28)]
        [string]$GmsaGroupName,
        [parameter(Mandatory = $false, ParameterSetName = "gmsaAccount")]
        [parameter(Mandatory = $false, ParameterSetName = "standardAccount")]
        [string]$BaseDn,
        [parameter(Mandatory = $false, ParameterSetName = "standardAccount")]
        [switch]$ForceStandardAccount,
        [parameter(DontShow,Mandatory = $false, ParameterSetName = "gmsaAccount")]
        [parameter(DontShow,Mandatory = $false, ParameterSetName = "standardAccount")]
        [string]$domain,
        [parameter(DontShow,Mandatory = $false, ParameterSetName = "gmsaAccount")]
        [parameter(DontShow,Mandatory = $false, ParameterSetName = "standardAccount")]
        $myDomain
    )

    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
    $returnVal = $false
    if ($Identity -match '.*\$') {
        $Identity = $Identity.replace('$', '')
    }

    if (($myDomain.forestSid -ne $myDomain.domainSid) -and ($IdentityLocation -eq 'Forest') -and (-not (Test-MDIUserInEnterpriseAdmins))) {
        write-warning "Must be ENTERPRISE ADMIN to work with KDS in a child domain!"
        throw
    }

    if (($myDomain.forestSid -ne $myDomain.domainSid) -and ($IdentityLocation -eq 'Forest')) {
        $myDomain = Initialize-MyDomain -domain $myDomain.forestFqdn
    }

    if ([string]::IsNullOrEmpty($baseDn)) {
        $baseDn = $myDomain.usersContainer
        $msaLocation = '{0},{1}' -f "CN=Managed Service Accounts",$myDomain.domainDn
    } else {
        $msaLocation = $baseDn
    }
    if ($forceStandardAccount) {
        try {
            if ([string]::IsNullOrEmpty($myDomain.GetObjectByFilter("samaccountname=$Identity"))) {
                throw
            }
        } catch {
            try {
                if (-not [string]::IsNullOrEmpty($mydomain.NewAdUser($baseDn,$Identity,"This account runs the MDI service"))) {
                    $returnVal = $true
                }
            } catch {
                Write-Error "Failed to create standard service account"
            }
        }
    } else {
        if (-not (Test-MDIKDSRootKey -myDomain $myDomain)) {
            if ($eaGroupName -in $groups) {
                try {
                    $null = Add-KdsRootKey -EffectiveTime ((Get-Date).AddHours(-10))
                    write-output "Created KDS root key"
                } catch {
                    throw "Failed to create KDS root key. If this is a child domain you need to be ENTERPRISE ADMIN to write the forest KDS!"
                }
            }
        } else {
            Write-Verbose "KDS Root Key detected"
        }
        if (Test-MDIKDSRootKey -myDomain $myDomain) {
            $baseGroupDn = $myDomain.usersContainer
            if (!($myDomain.GetObjectByName($GmsaGroupName,"domain",$false))) {
                $gmsaGroupCreate = $myDomain.NewAdGroup($baseGroupDn,$GmsaGroupName,("Members of this group are computer objects allowed to retrieve the managed password for {0}" -f $Identity),"UNIVERSAL")
                if ($null -eq $gmsaGroupCreate) {
                    $truncatedGmsaGroupName = "{0}-GMSAPwdRet" -f $(($Identity).replace('$','').substring(0, ([System.Math]::Min(16, ($Identity).Length)-1)))
                    $gmsaGroupCreate = $myDomain.NewAdGroup($baseGroupDn,$truncatedGmsaGroupName,("Members of this group are computer objects allowed to retrieve the managed password for {0}" -f $Identity),"UNIVERSAL")
                }
                if (-not [string]::IsNullOrEmpty($gmsaGroupCreate)) {
                    Write-Verbose "Created GMSA group"
                    if ($IdentityLocation -eq "Forest") {
                        $mydomain.adForest.domains | Where-Object {
                            $_ -ne $mydomain.domainfqdn
                        } | Foreach { 
                            $childDomain = Initialize-MyDomain -domain $_ -myDomain $null
                            $null = $childDomain.AddObjectToGroup($($childDomain.privilegedGroupNames["domain controllers"]),"domain",$($gmsaGroupCreate.Properties["samaccountname"]),"forest")
                        }
                    }
                } else {
                    Write-Error "Failed to create GMSA group"
                }
            }
            start-sleep -seconds 1
            $newAdServiceAccountParams = @{
                Name = $Identity
                DNSHostName = "$($myDomain.domainFqdn)"
                PrincipalsAllowedToRetrieveManagedPassword = @()
                SamAccountName = $Identity
                Server = $($myDomain.chosenDc)
                Path = $msaLocation
            }
            if (-not [string]::IsNullOrEmpty($($myDomain.privilegedGroupNames["Domain Controllers"]))) {
                $newAdServiceAccountParams.PrincipalsAllowedToRetrieveManagedPassword += $($myDomain.privilegedGroupNames["Domain Controllers"])
            }
            if ($null -ne $gmsaGroupCreate) {
                $newAdServiceAccountParams.PrincipalsAllowedToRetrieveManagedPassword += $($gmsaGroupCreate.Properties["samaccountname"])
            }
            try {
                $serviceAccount = New-ADServiceAccount @newAdServiceAccountParams -PassThru
                $returnVal = $true
                write-verbose "Created service account"
            } catch {
                try {
                    $newAdServiceAccountParams["Path"] = $myDomain.usersContainer
                    $serviceAccount = New-ADServiceAccount @newAdServiceAccountParams -PassThru
                    $returnVal = $true
                    write-verbose "Created service account"
                } catch {
                    throw
                }
                write-error "Failed to create GMSA account"
            }
        } else {
            write-warning "KDS root key not detected"
        }
    }
    return $returnVal
}
function New-MdiGpo {
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$true)] # make sure to add a reference for any new GPO here
        [ValidateSet('AdvancedAuditPolicyCAs', 'AdvancedAuditPolicyDCs', 'DeployGpo', 'EntraIDAuditing',
            'LogonAsService', 'NTLMAuditing', 'PerformanceLib', 'ProcessorPerformance', 'RemoveGpo', 'All')] [string[]]$gposToCreate,
        [parameter(Mandatory=$false)]
        [switch]$Force,
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )

    DynamicParam {
        $paramDictionary = New-Object -Type System.Management.Automation.RuntimeDefinedParameterDictionary
        # Define parameter attributes
        $paramAttributes = New-Object -Type System.Management.Automation.ParameterAttribute
        $paramAttributes.Mandatory = $true
        $paramAttributesCollect = New-Object -Type System.Collections.ObjectModel.Collection[System.Attribute]
        $paramAttributesCollect.Add($paramAttributes)
        $dynParamIdentity = New-Object -Type System.Management.Automation.RuntimeDefinedParameter("Identity", [string], $paramAttributesCollect)
        if ([bool](@('LogonAsService','All') -match $($gposToCreate -join '|'))) { 
            $paramDictionary.Add("Identity", $dynParamIdentity)
        }
        return $paramDictionary
    }

    begin {
        foreach ($key in $PSBoundParameters.Keys) {
      	    if ($MyInvocation.MyCommand.Parameters.$key.isDynamic) {
                Set-Variable -Name $key -Value $PSBoundParameters.$key
            }
    	}
    }

    process {
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Entering function New-MdiGpo" -logSev "Info" | out-null
        write-verbose "Entering function New-MdiGpo"
        $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
        if ([string]::IsNullOrEmpty($myDomain.writableSysvolPath)) {
            write-error "Failed to discover SYSVOL. See $env:temp\mircatmdi.log for details. Critical stop!"
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to discover SYSVOL $($_.Exception)" -logSev "Error" | out-null
            throw
        }
        # this line exists to get the manifest names for the GPO so we can keep the names in 1 place (the MdiDomainReadiness class) and not have to update them everywhere
        [MdiDomainReadiness]$myMdiDomainReadiness = [MdiDomainReadiness]::new($myDomain)
        $mdiGpoList = [System.Collections.Generic.List[MdiGpo]]::new()
        #GPOs to be created
        if (Use-GpoName $gposToCreate 'AdvancedAuditPolicyCAs') {$mdiGpoList.Add([MdiGpo]::new("$($myMdiDomainReadiness.AdvancedAuditPolicyCAs.GpoName)")) | out-null}
        if (Use-GpoName $gposToCreate 'AdvancedAuditPolicyDCs') {$mdiGpoList.Add([MdiGpo]::new("$($myMdiDomainReadiness.AdvancedAuditPolicyDCs.GpoName)")) | out-null}
        if (Use-GpoName $gposToCreate 'EntraIDAuditing') {$mdiGpoList.Add([MdiGpo]::new("$($myMdiDomainReadiness.EntraIDAuditing.GpoName)")) | out-null}
        if (Use-GpoName $gposToCreate 'LogonAsService') {$mdiGpoList.Add([MdiGpo]::new("$($myMdiDomainReadiness.LogonAsService.GpoName)")) | out-null}
        if (Use-GpoName $gposToCreate 'NTLMAuditing') {$mdiGpoList.Add([MdiGpo]::new("$($myMdiDomainReadiness.NTLMAuditing.GpoName)")) | out-null}
        if (Use-GpoName $gposToCreate 'PerformanceLib') {$mdiGpoList.Add([MdiGpo]::new("$($myMdiDomainReadiness.PerformanceLib.GpoName)")) | out-null}
        if (Use-GpoName $gposToCreate 'ProcessorPerformance') {$mdiGpoList.Add([MdiGpo]::new("$($myMdiDomainReadiness.ProcessorPerformance.GpoName)")) | out-null}
        #if (Use-GpoName $gposToCreate 'RemoteSAM') {$mdiGpoList.Add([MdiGpo]::new("$($myMdiDomainReadiness.RemoteSAM.GpoName)")) | out-null}
        #DeployGpo is special and won't be created by default "All" specification. you must ask for it by name
        if ("DeployGpo" -in $gposToCreate) {
            $mdiGpoList.Add([MdiGpo]::new("$($myMdiDomainReadiness.DeployGpo.GpoName)")) | out-null
        }
        if ("RemoveGpo" -in $gposToCreate) {
            $mdiGpoList.Add([MdiGpo]::new("$($myMdiDomainReadiness.RemoveGpo.GpoName)")) | out-null
        }
        foreach ($mdiGpo in $mdiGpoList) {
            try {
                $guid=($myDomain.GetGpo("$($mdiGpo.name)",$null)).Properties["cn"].trimstart('{').trimend('}')
            } catch {
                $guid = $null
            }
            if ($guid) {
                if ($Force) {
                    try {
                        if ($myDomain.RemoveGpo($null,$guid)) {
                            write-output "Removed GPO $($mdiGpo.name) in domain $($myDomain.domainDn) with GUID $guid"
                            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Removed GPO $($clawGpo.name) in domain $($myDomain.domainDn) with GUID $guid" -logSev "Info" | out-null
                        } else {
                            throw
                        }
                    } catch {
                        $mdiGpoList.Find({param($x) $x.name -eq $mdiGpo.name}).guid = $guid
                        write-warning "Force was specified but removal failed for GPO $($clawGpoToCreate.name)"
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Force was specified but removal failed for GPO $($clawGpoToCreate.name). $errorMessage" -logSev "Warn" | out-null
                    }
                } else {
                    $mdiGpoList.Find({param($x) $x.name -eq $mdiGpo.name}).guid = $guid
                    write-output "GPO $($mdiGpo.name) already exists in domain $($myDomain.domainDn) with GUID $guid"
                    Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "GPO $($mdiGpo.name) already exists in domain $($myDomain.domainDn) with GUID $guid" -logSev "Info" | out-null
                }
            } else {
                write-verbose "No GUID found for GPO $($mdiGpo.name)"
                Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "No GUID found for GPO $($mdiGpo.name)" -logSev "Info" | out-null
            }
            $guid = $null
        }

        # foreach gpo to create in the list of GPO's where the GUID is length 0 (e.g. not found)
        foreach ($mdiGpoToCreate in $mdiGpoList.FindAll({param($x) $x.guid.length -eq 0})) {
            switch ($mdiGpoToCreate.name) {
                "$($myMdiDomainReadiness.AdvancedAuditPolicyCAs.GpoName)"  {
                    $createdGpo = New-MdiGpoToCreate -mdiGpoToCreate $($mdiGpoList.Find({param($x) $x.name -eq "$($myMdiDomainReadiness.AdvancedAuditPolicyCAs.GpoName)"})) -myDomain $mydomain
                    if ($createdGpo.created -eq $true) {
                        Set-MdiAuditAdcsGpo -createdGpo $createdGpo -myDomain $myDomain
                    } else {
                        write-warning "$($mdiGpoToCreate.name) reported creation failure so we're not setting content."
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($mdiGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                    }
                }
                "$($myMdiDomainReadiness.AdvancedAuditPolicyDCs.GpoName)"  {
                    $createdGpo = New-MdiGpoToCreate -mdiGpoToCreate $($mdiGpoList.Find({param($x) $x.name -eq "$($myMdiDomainReadiness.AdvancedAuditPolicyDCs.GpoName)"})) -myDomain $mydomain
                    if ($createdGpo.created -eq $true) {
                        Set-MdiAuditDcGpo -createdGpo $createdGpo -myDomain $myDomain
                    } else {
                        write-warning "$($mdiGpoToCreate.name) reported creation failure so we're not setting content."
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($mdiGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                    }
                }
                "$($myMdiDomainReadiness.PerformanceLib.GpoName)"  {
                    $createdGpo = New-MdiGpoToCreate -mdiGpoToCreate $($mdiGpoList.Find({param($x) $x.name -eq "$($myMdiDomainReadiness.PerformanceLib.GpoName)"})) -myDomain $mydomain
                    if ($createdGpo.created -eq $true) {
                        Set-MdiPerfLibGpo -createdGpo $createdGpo -myDomain $myDomain
                    } else {
                        write-warning "$($mdiGpoToCreate.name) reported creation failure so we're not setting content."
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($mdiGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                    }
                }
                "$($myMdiDomainReadiness.ProcessorPerformance.GpoName)"  {
                    $createdGpo = New-MdiGpoToCreate -mdiGpoToCreate $($mdiGpoList.Find({param($x) $x.name -eq "$($myMdiDomainReadiness.ProcessorPerformance.GpoName)"})) -myDomain $mydomain
                    if ($createdGpo.created -eq $true) {
                        Set-MdiPerfPlanGpo -createdGpo $createdGpo -myDomain $myDomain
                    } else {
                        write-warning "$($mdiGpoToCreate.name) reported creation failure so we're not setting content."
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($mdiGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                    }
                }
                "$($myMdiDomainReadiness.LogonAsService.GpoName)"  {
                    $createdGpo = New-MdiGpoToCreate -mdiGpoToCreate $($mdiGpoList.Find({param($x) $x.name -eq "$($myMdiDomainReadiness.LogonAsService.GpoName)"})) -myDomain $mydomain
                    if ($createdGpo.created -eq $true) {
                        Set-MdiLogonServiceGpo -createdGpo $createdGpo -myDomain $myDomain -Identity $identity
                    } else {
                        write-warning "$($mdiGpoToCreate.name) reported creation failure so we're not setting content."
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($mdiGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                    }
                }
                "$($myMdiDomainReadiness.NTLMAuditing.GpoName)"  {
                    $createdGpo = New-MdiGpoToCreate -mdiGpoToCreate $($mdiGpoList.Find({param($x) $x.name -eq "$($myMdiDomainReadiness.NTLMAuditing.GpoName)"})) -myDomain $mydomain
                    if ($createdGpo.created -eq $true) {
                        Set-MdiNtlmAuditDcGpo -createdGpo $createdGpo -myDomain $myDomain
                    } else {
                        write-warning "$($mdiGpoToCreate.name) reported creation failure so we're not setting content."
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($mdiGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                    }
                }
                "$($myMdiDomainReadiness.RemoteSAM.GpoName)"  {
                    $createdGpo = New-MdiGpoToCreate -mdiGpoToCreate $($mdiGpoList.Find({param($x) $x.name -eq "$($myMdiDomainReadiness.RemoteSAM.GpoName)"})) -myDomain $mydomain
                    if ($createdGpo.created -eq $true) {
                        Set-MdiSamrGpo -createdGpo $createdGpo -myDomain $myDomain -Identity $identity
                    } else {
                        write-warning "$($mdiGpoToCreate.name) reported creation failure so we're not setting content."
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($mdiGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                    }
                }
                "$($myMdiDomainReadiness.EntraIDAuditing.GpoName)" {
                    $createdGpo = New-MdiGpoToCreate -mdiGpoToCreate $($mdiGpoList.Find({param($x) $x.name -eq "$($myMdiDomainReadiness.EntraIDAuditing.GpoName)"})) -myDomain $mydomain
                    if ($createdGpo.created -eq $true) {
                        Set-MdiEntraAuditGpo -createdGpo $createdGpo -myDomain $myDomain
                    } else {
                        write-warning "$($mdiGpoToCreate.name) reported creation failure so we're not setting content."
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($mdiGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                    }
                }
                "$($myMdiDomainReadiness.DeployGpo.GpoName)" {
                    $createdGpo = New-MdiGpoToCreate -mdiGpoToCreate $($mdiGpoList.Find({param($x) $x.name -eq "$($myMdiDomainReadiness.DeployGpo.GpoName)"})) -myDomain $mydomain
                    if ($createdGpo.created -eq $true) {
                        Set-MdiDeployGpo -createdGpo $createdGpo -myDomain $myDomain
                    } else {
                        write-warning "$($mdiGpoToCreate.name) reported creation failure so we're not setting content."
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($mdiGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                    }
                }
                "$($myMdiDomainReadiness.RemoveGpo.GpoName)" {
                    $createdGpo = New-MdiGpoToCreate -mdiGpoToCreate $($mdiGpoList.Find({param($x) $x.name -eq "$($myMdiDomainReadiness.RemoveGpo.GpoName)"})) -myDomain $mydomain
                    if ($createdGpo.created -eq $true) {
                        Set-MdiRemoveGpo -createdGpo $createdGpo -myDomain $myDomain
                    } else {
                        write-warning "$($mdiGpoToCreate.name) reported creation failure so we're not setting content."
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($mdiGpoToCreate.name) reported creation failure so we're not setting content in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Warn" | out-null
                    }
                }
            }    
        }
    }
    
}
function New-MdiGpoToCreate
{
    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$true)]
        $mdiGpoToCreate,
        [Parameter(Mandatory=$true)]
        $myDomain
    )
    # create block is constant
    try {
        $create = $myDomain.NewGpo("$($mdiGpoToCreate.name)",1)
        if (!($create)) {
            throw 
        }
    } catch {
        write-error "Attempt to create $($mdiGpoToCreate.name) failed because returned GUID was null"
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Attempt to create $($mdiGpoToCreate.name) failed because returned GUID was null" -logSev "Error" | out-null
    }
    if ($create) {
        $mdiGpoToCreate.guid = $create
        $mdiGpoToCreate.created = $true
        write-host "Created $($mdiGpoToCreate.name) in domain $($myDomain.domainFqdn)"
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Created $($mdiGpoToCreate.name) in domain $($myDomain.domainFqdn)" -logSev "Info" | out-null
    } else {
        write-error "Failed to create $($mdiGpoToCreate.name) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to create $($mdiGpoToCreate.name) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    start-sleep 3 #for repl

    #return the modified object
    return $mdiGpoToCreate
}
function New-PawGroups {
    [CmdletBinding()]
    Param(
        [Parameter(DontShow)]
        $myEntra
    )
    Add-LogEntry -logFilePath $env:temp\paw.log -logMessage "Entering function New-PawGroups" -logSev "Info" | out-null
    write-verbose "Entering function New-PawGroups"
    if ($myEntra -eq $null) {
        try {
            
            if (!($myEntra)) {
                throw
            }
        } catch {
            write-error "Failed to discover Entra. See $env:temp\paw.log for details. Critical stop!"
        	Add-LogEntry -logFilePath $env:temp\paw.log -logMessage "Failed to discover AD $($_.Exception)" -logSev "Error" | out-null
        	throw
        }
    } else {
        
    }
    
}
function New-PawUsers {
    [CmdletBinding()]
    Param(
        [Parameter(DontShow)]
        $myEntra
    )

}
function New-RandomPassword 
{
    $guid = (new-guid).guid.split('-')
    $guid[0]+=[char](get-random -min 65 -max 90)
    $guid[1]+=[char](get-random -min 65 -max 90)
    $password = $guid -join '-'
    return $password
}
function Remove-AdAdminCount
{
    ##########################################################################################################
    <#
    .SYNOPSIS
        Removes the adminCount=1 from non-admin accounts.
    
    .DESCRIPTION
        Run Remove-AdAdminCount and it will seek and fix.


    .EXAMPLE
        Remove-AdAdminCount

    .OUTPUTS
        Writes to console

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

    [CmdletBinding(DefaultParameterSetName = 'singleUser')]
    param(
        [Parameter(Mandatory = $false,ValueFromPipeline = $true,ParameterSetName='pipeline',DontShow)]
        [string]$pipelineInput,
        [Parameter(Mandatory = $true,ParameterSetName='singleUser')]
        [string]$Identity,
        [Parameter(Mandatory = $false,ParameterSetName='allUsers')]
        [switch]$AllUsers,
        [parameter(Mandatory = $false, ParameterSetName = "singleUser")]
        [parameter(Mandatory = $false, ParameterSetName = "allUsers")]
        [switch]$AlsoClearSPN,
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter()]
        [switch]$WhatIf=$false,
        [Parameter(DontShow)]
        $myDomain
    )
    begin {
        $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
        $actionableUsers = [System.Collections.Generic.List[String]]::new()
    }
    process {
        if ($PSCmdlet.ParameterSetName -eq 'pipeline') {
            $actionableUsers.Add($pipelineInput)
        }
        if ($Identity) {
            if ($Identity.Contains('=')) {
                $actionableUsers.Add($Identity)
            } else {
                $idCheck = ($myDomain.GetObjectByName("$Identity","domain",$false))
                if ($idCheck) {
                    $actionableUsers.Add($($idCheck.properties.distinguishedname))
                } else {
                    write-warning "Provided identity $Identity not found in AD."
                }
            }
        }
        if ($AllUsers) {
            Get-AdAdminCount -myDomain $myDomain | % { $actionableUsers.Add($_)}
        }
        $privilegedGroups = $myDomain.privilegedgroupnames.values[0].split("`n")
        if ($actionableUsers.count -gt 0) {
            foreach ($actionableUser in $actionableUsers) {
                # make sure it's not in priv groups. even though the get-adadmincount knows about this, a manually passed in user may not
                # but only do this for singleUser mode for efficiency
                if ($PsCmdlet.ParameterSetName -eq "singleUser") {
                    $memberOfArray=@()
                    if ([bool]$idCheck.Properties.memberof) {
                        $idCheck.Properties.memberof | % { $memberOfArray += ($_.split(',')[0]).trim('CN=') }
                        $memberOfArray
                        $check=[bool]($memberOfArray | ? -FilterScript {$_ -in $privilegedGroups})
                    } else {
                        $check = $false
                    }
                } else {
                    $check = $false
                }
                if (!($check)) {
                    try {
                        if ($WhatIf) {
                            write-output $('What if: Performing the operation "Set" on target {0}.' -f $actionableUser)
                        } else {
                            if ($myDomain.SetObjectByDn($actionableUser,"adminCount",0,"domain")) {
                                write-output $('Performing the operation "Set" on target {0}' -f $actionableUser)
                                $null = $myDomain.ResetAdAcl($actionableUser,$false,$true,$true)
                                if ($AlsoClearSPN) {
                                    if (-not $myDomain.SetObjectByDn($actionableUser,"servicePrincipalName",$null,"domain")) {
                                        throw
                                    }
                                }
                            } else {
                                throw
                            }
                        }
                    } catch {
                        write-warning "Failed to set adminCount=0 for $actionableUser"
                        write-warning "Are you running as admin?"
                    }
                } else {
                    write-host "$actionableUser is a member of a privileged group and won't be modified."
                }
            }
        }
        # do not remove or move this
        if ($PSCmdlet.ParameterSetName -eq 'pipeline') {
            $actionableUsers.Remove($pipelineInput) | out-null
        }
    }
    
    
}
Function Remove-EntraAppOwners
{
  <#
    .SYNOPSIS
    This script retrieves and manages the owners of Azure AD applications 
    and service principals using Microsoft Graph API.

    .DESCRIPTION
    The script connects to the Microsoft Graph API using the provided tenant ID 
    and retrieves the owners of all applications and service principals in the 
    Azure AD tenant. It then saves the owner information in a JSON file. Optionally, 
    it can delete the owners of the applications and service principals or restore 
    the owners from a previously saved JSON file.

    .PARAMETER tenantid
    The ID of the Azure AD tenant.

    .PARAMETER exportFile
    The name of the JSON file to save the owner information. Default value 
    is "AppObjectOwners.json".

    .PARAMETER delete
    Switch parameter to indicate whether to delete the owners of the applications 
    and service principals.

    .PARAMETER restore
    Switch parameter to indicate whether to restore the owners from a previously 
    saved JSON file.

    .EXAMPLE
    .\Remove-EntraAppOwners.ps1 -tenantid "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" -exportFile "Owners.json" -delete

    This example connects to the Microsoft Graph API using the specified tenant ID, 
    retrieves the owners of all applications and service principals, saves the owner 
    information in a JSON file named "Owners.json", and deletes the owners.

    .EXAMPLE
    .\Remove-EntraAppOwners.ps1 -tenantid "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" -exportFile "Owners.json" -restore

    This example connects to the Microsoft Graph API using the specified tenant ID, 
    restores the owners of the applications and service principals from a previously 
    saved JSON file.

  #>

  [CmdletBinding()]
  param (
      [Parameter()]
      [string]
      $exportFile = "AppObjectOwners.json",

      [Parameter()]
      [switch]
      $Export,

      [Parameter()]
      [switch]
      $delete,

      [Parameter()]
      [switch]
      $restore
  )

  if ($restore -and $delete)
  {
    Write-error -Message "Cannot use both -delete and -restore switches."
    exit
  }

  #region export
  if ($Export)
  {
    $objectTypes = "servicePrincipal", "application"
    $ownerInfo = @()

    Foreach ($type in $objectTypes)
    {
      [array]$collection = Get-EntraObject -objectType $type

      Foreach ($app in $collection)
      {

        [array]$appOwners = Get-EntraObjectOwner -objectType $type -objectId $($app.id)
        
        If ($appOwners.Count -ne 0)
        {
          $owners = @()
          foreach ($o in $appOwners)
          {
            $owner = new-object psobject
            $owner | Add-Member -MemberType NoteProperty -Name "id" -Value $o.id
            $owner | Add-Member -MemberType NoteProperty -Name "displayName" -Value $o.displayName
            $owner | Add-Member -MemberType NoteProperty -Name "userPrincipalName" -Value $o.userPrincipalName
            $owners += $owner
          }

          $item = New-Object PSObject
          $item | Add-Member -MemberType NoteProperty -Name "objectType" -Value $type
          $item | Add-Member -MemberType NoteProperty -Name "displayName" -Value $app.displayName
          $item | Add-Member -MemberType NoteProperty -Name "objectId" -Value $app.Id
          $item | Add-Member -MemberType NoteProperty -Name "appId" -Value $app.appId
          $item | Add-Member -MemberType NoteProperty -Name "owners" -Value $owners
          $ownerInfo += $item
        }
      }
    }
  
    if (Test-Path $exportFile) 
    {
      $date = Get-Date -Format "yyyyMMddHHmmss"
      Rename-Item $exportFile -NewName "$exportFile-$date.json"
    }

    $ownerInfo | ConvertTo-Json -Depth 3 | out-file $exportFile
  }
  #endregion export

  #region delete
  If ($delete)
  {
    $Data = Get-Content $exportFile | ConvertFrom-Json
    Foreach ($app in $Data)
    {
      Foreach ($owner in $app.owners)
      {
        Remove-EntraObjectOwner -objectType $app.objectType -objectId $app.objectId -ownerID $owner.id
      }
    }
  }
  #endregion delete

  #region restore
  If($restore)
  {
    $restoreData = Get-Content $exportFile | ConvertFrom-Json
    Foreach ($app in $restoreData)
    {
      Foreach ($owner in $app.owners)
      {
        Add-EntraObjectOwner -objectType $app.objectType -objectId $app.objectId -ownerID $owner.id
      }
    }
  }
  #endregion restore
}
function Remove-EntraObject
{
  # Example usage:
  # Remove-EntraObject -objectType user -objectId "1d3b3e1f-2b4e-4b1e-8f4d-0f4f1f4f1f4f"

  [CmdletBinding()]
  param
  (
    [Parameter(Mandatory = $true)]
    #[ValidateSet([graphObjectTypeValidator])] # This is not working in powershell 5.0
    [ValidateScript({
      $validValues = [graphObjectTypePermissions]::new().getAll().Keys
      if ($_ -in $validValues) {
          $true
      } else {
          throw "Invalid value '$_'. Valid values are: $($validValues -join ', ')"
      }
    })]
    [string] $objectType,
  
    [Parameter(Mandatory = $true)]
    [string] $objectId
  )

  $graphCollection = [GraphAL]::new(@{objectType = $objectType})
  $graphCollection.Delete(@{objectId = $objectId})
}
function Remove-EntraObjectOwner
{
  <#
    .SYNOPSIS
    Removes the owner of an Entra object.

    .DESCRIPTION
    This function removes the specified owner from an Entra object.

    .PARAMETER objectType
    The type of the Entra object.

    .PARAMETER objectId
    The ID of the Entra object.

    .PARAMETER ownerID
    The ID of the owner to be removed.

    .EXAMPLE
    Remove-EntraObjectOwner -objectType "User" -objectId "7ba2fe02-58c3-47fa-a83f-6b8340b67e7c" -ownerID "236fdaee-9a6d-41ea-aab6-4dec83520a32"
    Removes the owner with ID "236fdaee-9a6d-41ea-aab6-4dec83520a32" from the Entra object with ID "7ba2fe02-58c3-47fa-a83f-6b8340b67e7c" of type "User".
  #>

  [CmdletBinding()]
  param
  (
    [Parameter(Mandatory = $true)]
    #[ValidateSet([graphObjectTypeValidator])] # This is not working in powershell 5.0
    [ValidateScript({
      $validValues = [graphObjectTypePermissions]::new().getAll().Keys
      if ($_ -in $validValues) {
          $true
      } else {
          throw "Invalid value '$_'. Valid values are: $($validValues -join ', ')"
      }
    })]
    [string] $objectType,

    [Parameter(Mandatory = $true)]
    [string] $objectId,

    [Parameter(Mandatory = $true)]
    [string] $ownerID
  )

  $graphCollection = [GraphAL]::new(@{objectType = $objectType})

  $params = @{
    objectId = $objectId
    ownerID = $ownerID
  }

  Try
  {
    $graphCollection.deleteOwner($params)
  }
  Catch
  {
    Write-Error $_.Exception.Message
  }
}
function Remove-EntraPrivilegedRoleMembers
{
  [CmdletBinding()]
  param (
  <#    [Parameter(mandatory = $true)]
      [string]
      $tenantid,
  #>
      [Parameter()]
      [string]
      $dataFile,

      [Parameter()]
      [string]
      $exemptedUsers,

      [parameter()]
      [switch]
      $remove,

      [Parameter()]
      [switch]
      $restore,

      [Parameter()]
      [string]
      $apiversion = "beta" 
      <# 
        The beta api has a flag for privileged roles:
        https://learn.microsoft.com/en-us/entra/identity/role-based-access-control/privileged-roles-permissions?tabs=ms-graph
      #>
        )

  $roleManagement = [entraRoleManagement]::new(@{apiVersion=$apiversion;serviceName="directory";componentName="roleDefinitions"})
  $roleAssignments = [entraRoleManagement]::new(@{apiVersion=$apiversion;serviceName="directory";componentName="roleAssignments"})
  $roleEligibilitySchedules = [entraRoleManagement]::new(@{apiVersion=$apiversion;serviceName="directory";componentName="roleEligibilitySchedules"})

  if ($remove -and $restore)
  {
    Write-Error -Message "Cannot use both -remove and -restore at the same time."
    exit
  }

  #region User exemption
  # Get current user to ensure we are not cutting the branch we are sitting on
  #$me = (Get-MgContext).Account
  if ($null -eq $graphalContext)
  {
    Write-Error -Message "Failed to get graphal context."
    exit
  } Else {
    $me = $graphalContext.userId
  }
  
  if ($null -eq $me)
  {
    Write-Error -Message "Failed to get current user."
    exit
  } else {
    $currentuser = get-EntraObject -objectType User -objectId $me
  }

  $exemptedUsersList = @()
  $exemptedUsersList += $currentUser.userPrincipalName # Add current user to exempted users list

  if ($exemptedUsers.Contains(',')) 
  {
    $exemptedUsersList = $exemptedUsers.split(',').trim()
  } else {
    $exemptedUsersList += $exemptedUsers
  }

  # Validate exempted users existance
  If ($exemptedUsersList.Count -gt 0)
  {
    foreach ($exemptedUser in $exemptedUsersList)
    {
      $user = get-EntraObject -objectType User -objectId $exemptedUser
      if ($null -eq $user)
      {
        Write-Error -Message "User $exemptedUser does not exist in the tenant."
        exit
      }
    }
  } Else {
      Write-Error -Message "No users excluded."
      exit
  }

  # Check that at least one user is kept as a member of Global Admins
  $globalAdmins = $roleManagement.get(@{filter="displayName eq 'Global Administrator'"}).value
  $GAMembers = $roleAssignments.get(@{filter="roleDefinitionId eq '$($globalAdmins.id)'"}).value

  If (-not $GAMembers.principalid -contains $currentuser.id)
  {
    Write-Error -Message "You must run this as a Global Administrator"
    exit
  }

  Write-host "These members will keep administrative privileges:" -ForegroundColor Cyan
  Write-host `t $exemptedUsersList -ForegroundColor Green

  #endregion User exemption

  If (!($remove -OR $restore))
  {
    #region process privileged roles
    # $uri = "https://graph.microsoft.com/beta/roleManagement/directory/roleDefinitions?`$filter=isPrivileged eq true"
    $privilegedRoleDefinitions = $roleManagement.get(@{filter="isPrivileged eq true"}).value
    $privilegedRoleAssignments = @()

    Foreach ($role in $privilegedRoleDefinitions)
    {
      # Walk through static role assignments
      #$uri = "https://graph.microsoft.com/$apiversion/roleManagement/directory/roleAssignments?`$filter=roleDefinitionId eq '$($role.id)'&`$expand=principal"
      $roleMembers = $roleAssignments.get(@{filter="roleDefinitionId eq '$($role.Id)'";additionalParameters="&`$expand=principal"}).value
      Foreach ($roleMember in $roleMembers)
      {
        #$upn = (Invoke-GraphRequest -Uri "https://graph.microsoft.com/$apiversion/users/$($roleMember.principalId)").userPrincipalName
        
        $roleAssignment = new-object psobject
        $roleAssignment | add-member -membertype NoteProperty -name "assignmentId" -value $roleMember.id
        $roleAssignment | add-member -membertype NoteProperty -name "assignmentType" -value "Static"
        $roleAssignment | add-member -membertype NoteProperty -name "roleId" -value $role.id
        $roleAssignment | add-member -membertype NoteProperty -name "roleDisplayName" -value $role.displayName
        $roleAssignment | add-member -membertype NoteProperty -name "directoryScopeId" -value $roleMember.directoryScopeId
        $roleAssignment | add-member -membertype NoteProperty -name "roleMemberType" -value ($roleMember.principal.'@odata.type').Split('.')[-1]
        $roleAssignment | add-member -membertype NoteProperty -name "roleMemberId" -value $roleMember.principal.Id
        $roleAssignment | add-member -membertype NoteProperty -name "roleMemberDisplayName" -value $roleMember.principal.displayName

        if (($roleMember.principal.'@odata.type').Split('.')[-1] -eq "user")
        {
          $roleAssignment | add-member -membertype NoteProperty -name "roleMemberUpn" -value $roleMember.principal.userPrincipalName
        }

        if (($roleMember.principal.'@odata.type').Split('.')[-1] -eq "servicePrincipal")
        {
          $roleAssignment | add-member -membertype NoteProperty -name "appId" -value $roleMember.principal.appId
          $roleAssignment | add-member -membertype NoteProperty -name "appOwnerOrganizationId" -value $roleMember.principal.appOwnerOrganizationId
        }

        $privilegedRoleAssignments += $roleAssignment
      } # end loop static members

      # start loop eligible members
      $roleEligibleMembers = $roleEligibilitySchedules.get(@{filter="roleDefinitionId eq '$($role.Id)'"}).value
      If ($null -ne $roleEligibleMembers -and $roleEligibleMembers.gettype() -eq "Hashtable" )
      {
        # In this case the role has only one eligible member, no need to loop through
        $roleAssignment = new-object psobject
        $roleAssignment | add-member -membertype NoteProperty -name "assignmentId" -value $roleEligibleMembers.id
        $roleAssignment | add-member -membertype NoteProperty -name "assignmentType" -value "Eligible"
        $roleAssignment | add-member -membertype NoteProperty -name "roleId" -value $role.id
        $roleAssignment | add-member -membertype NoteProperty -name "roleDisplayName" -value $role.displayName
        $roleAssignment | add-member -membertype NoteProperty -name "directoryScopeId" -value $roleEligibleMembers.directoryScopeId
      
        $member = Get-EntraObject -objectType directoryObject -objectId $roleEligibleMembers.principalId
        $roleAssignment | add-member -membertype NoteProperty -name "roleMemberType" -value ($member.'@odata.type').Split('.')[-1]
        $roleAssignment | add-member -membertype NoteProperty -name "roleMemberId" -value $member.Id
        $roleAssignment | add-member -membertype NoteProperty -name "roleMemberDisplayName" -value $member.displayName

        if (($Member.'@odata.type').Split('.')[-1] -eq "user")
        {
          $roleAssignment | add-member -membertype NoteProperty -name "roleMemberUpn" -value $member.userPrincipalName
        }
    
        $privilegedRoleAssignments += $roleAssignment

      } Elseif ($null -ne $roleEligibleMembers -and $roleEligibleMembers.gettype() -ne "Hashtable") {

        # In this case the role has multiple eligible members
        Foreach ($eligible in $roleEligibleMembers)
        {
          $roleAssignment = new-object psobject
          $roleAssignment | add-member -membertype NoteProperty -name "assignmentId" -value $eligible.id
          $roleAssignment | add-member -membertype NoteProperty -name "assignmentType" -value "Eligible"
          $roleAssignment | add-member -membertype NoteProperty -name "roleId" -value $role.id
          $roleAssignment | add-member -membertype NoteProperty -name "roleDisplayName" -value $role.displayName
          $roleAssignment | add-member -membertype NoteProperty -name "directoryScopeId" -value $eligible.directoryScopeId
          $member = Get-EntraObject -objectType directoryObject -objectId $eligible.principalId
          $roleAssignment | add-member -membertype NoteProperty -name "roleMemberType" -value ($member.'@odata.type').Split('.')[-1]
          $roleAssignment | add-member -membertype NoteProperty -name "roleMemberId" -value $member.Id
          $roleAssignment | add-member -membertype NoteProperty -name "roleMemberDisplayName" -value $member.displayName

          if (($Member.'@odata.type').Split('.')[-1] -eq "user")
          {
            $roleAssignment | add-member -membertype NoteProperty -name "roleMemberUpn" -value $member.userPrincipalName
          }
      
          $privilegedRoleAssignments += $roleAssignment
        } # end loop parsing eligible members
      }

    } # end loop walking through roles
    #endregion process privileged roles

    #region save to file
    If (test-path $dataFile)
    {
      $date = Get-Date -Format "yyyyMMddHHmmss"
      $filename = $datafile.split("\")[$datafile.split("\").count-1]
      Rename-Item $dataFile -NewName $filename.Replace(".json","_$date.json")
    }
    $privilegedRoleAssignments | ConvertTo-Json | Out-File -FilePath $dataFile
    #endregion save to file
  } # End of if statement (no remove, no restore)

  #region remove privileged role assignments
  If($Remove)
  {
    if ([string]::IsNullOrEmpty($datafile))
    {
      write-error "No data file specified."
      exit
    }

    $removeData = Get-Content $dataFile | ConvertFrom-Json

    # Removing all privileged role assignments
    Foreach ($assignment in $removeData)
    {
      if ($exemptedUsersList -notcontains $assignment.roleMemberUpn)
      {
        if (!$null -eq $assignment.roleMemberUPN){$member = $assignment.roleMemberUpn} else {$member = $assignment.roleMemberDisplayName}
        Write-Host "Removing $($assignment.assignmentType) $($assignment.roleMemberType) `'$member`' from `'$($assignment.roleDisplayName)`' role." -ForegroundColor Red
        if ($($assignment.assignmentType) -eq "Static")
        {
          #Invoke-GraphRequest -Uri "https://graph.microsoft.com/$apiversion/roleManagement/directory/roleAssignments/$($assignment.roleid)/members/$($assignment.roleMemberId)/$ref" -Method DELETE
          $roleAssignments.delete(@{objectId=$($assignment.assignmentId)})
        } ElseIf ($assignment.assignmentType -eq "Eligible") {
          
          ###
          # NOTE: This is not possible, the Graph objecttype does not support delete operations
          ###

          #Invoke-GraphRequest -Uri "https://graph.microsoft.com/$apiversion/roleManagement/directory/roleEligibilitySchedules/$($assignment.roleid)/members/$($assignment.roleMemberId)/$ref" -Method DELETE
          #$roleEligibilitySchedules.delete(@{objectId=$($assignment.assignmentId)})
        }
      }
    }
  }
  #endregion remove privileged role assignments

  #region restore privileged role assignments
  if ($restore)
  {
    if ([string]::IsNullOrEmpty($datafile))
    {
      write-error "No data file specified."
      exit
    }

    $restoreData = Get-Content $dataFile | ConvertFrom-Json

    Foreach ($Assignment in $restoreData)
    {
      if ($assignment.assignmentType -eq "Static")
      {
        if (!$null -eq $assignment.roleMemberUPN){$member = $assignment.roleMemberUpn} else {$member = $assignment.roleMemberDisplayName}
        Write-Host "Restoring $($assignment.assignmentType) $($assignment.roleMemberType) `'$member`' to `'$($assignment.roleDisplayName)`' role." -ForegroundColor Blue
        $postbody = @{
          "@odata.type" = "#microsoft.graph.unifiedRoleAssignment"
          "roleDefinitionId" = $($Assignment.roleId)
          "principalId" = $($Assignment.roleMemberId)
          "directoryScopeId" = "/"
        }
        $roleAssignments.Create(@{postbody=$postbody})
      } ElseIf ($assignment.assignmentType -eq "Eligible") {
        ###
        # NOTE: This is not possible, the Graph objecttype does not support POST operations
        ###
      }

    }
  }
  #endregion restore privileged role assignments
}
function Remove-MDIDeploy {
    [CmdletBinding()]
    Param(
        [parameter(Mandatory=$true)]
        [ValidateSet("CreateUninstallGPO","RemoveDeletedObjectsPermissions","RemoveIdentity","RemoveInstallationFolder","RemoveMDIConfigurationGPOs","All")][string[]]$Actions,
        [Parameter(Mandatory=$false)]
        [switch]$WhatIf,
        [parameter(Mandatory=$false)]
        [switch]$Force,
        [Parameter(DontShow)]
        [string]$Domain,
        [Parameter(DontShow)]
        $myDomain
    )
    DynamicParam {
        $paramDictionary = New-Object -Type System.Management.Automation.RuntimeDefinedParameterDictionary
        # Define parameter attributes
        $paramAttributes = New-Object -Type System.Management.Automation.ParameterAttribute
        $paramAttributes.Mandatory = $true
        $paramAttributesCollect = New-Object -Type System.Collections.ObjectModel.Collection[System.Attribute]
        $paramAttributesCollect.Add($paramAttributes)
        $dynParamIdentity = New-Object -Type System.Management.Automation.RuntimeDefinedParameter("Identity", [string], $paramAttributesCollect)
        $paramAttributes = New-Object -Type System.Management.Automation.ParameterAttribute
        $paramAttributes.Mandatory = $true
        $paramAttributesCollect = New-Object -Type System.Collections.ObjectModel.Collection[System.Attribute]
        $paramAttributesCollect.Add($paramAttributes)
        $paramAttributesCollect.Add((New-Object System.Management.Automation.ValidateSetAttribute('Domain', 'Forest')))
        $dynParamIdentityLocation = New-Object -Type System.Management.Automation.RuntimeDefinedParameter("IdentityLocation", [string], $paramAttributesCollect)
        if ([bool](@('RemoveDeletedObjectsPermissions','RemoveIdentity','All') -match $($Actions -join '|'))) { 
            $paramDictionary.Add("Identity", $dynParamIdentity)
            $paramDictionary.Add("IdentityLocation", $dynParamIdentityLocation)
        }elseif ([bool](@('RemoveMDIConfigurationGPOs') -match $($Actions -join '|'))) { 
            $paramDictionary.Add("IdentityLocation", $dynParamIdentityLocation)
        }
        return $paramDictionary
    }
    begin {
        foreach ($key in $PSBoundParameters.Keys) {
      	    if ($MyInvocation.MyCommand.Parameters.$key.isDynamic) {
                Set-Variable -Name $key -Value $PSBoundParameters.$key
            }
    	}
    }
    process {
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Entering function Remove-MDIDeploy" -logSev "Info" | out-null
        write-verbose "Entering function Remove-MDIDeploy"
        $myDomain = Initialize-MyDomain -domain $Domain -myDomain $myDomain
        $myForest = Initialize-MyDomain -domain $($myDomain.forestFqdn) -myDomain $null
        # this line exists to get the manifest names for the GPO so we can keep the names in 1 place (the MdiDomainReadiness class) and not have to update them everywhere
        [MdiDomainReadiness]$myMdiDomainReadiness = [MdiDomainReadiness]::new($myDomain)
        $mdiGpoList = [System.Collections.Generic.List[MdiGpo]]::new()
        #GPOs inventory
        if (Use-GpoName $Actions 'RemoveMDIConfigurationGPOs') {
            $mdiGpoList.Add([MdiGpo]::new("$($myMdiDomainReadiness.AdvancedAuditPolicyCAs.GpoName)")) | out-null
            $mdiGpoList.Add([MdiGpo]::new("$($myMdiDomainReadiness.AdvancedAuditPolicyDCs.GpoName)")) | out-null
            $mdiGpoList.Add([MdiGpo]::new("$($myMdiDomainReadiness.EntraIDAuditing.GpoName)")) | out-null
            $mdiGpoList.Add([MdiGpo]::new("$($myMdiDomainReadiness.LogonAsService.GpoName)")) | out-null
            $mdiGpoList.Add([MdiGpo]::new("$($myMdiDomainReadiness.NTLMAuditing.GpoName)")) | out-null
            $mdiGpoList.Add([MdiGpo]::new("$($myMdiDomainReadiness.PerformanceLib.GpoName)")) | out-null
            $mdiGpoList.Add([MdiGpo]::new("$($myMdiDomainReadiness.ProcessorPerformance.GpoName)")) | out-null
            $mdiGpoList.Add([MdiGpo]::new("$($myMdiDomainReadiness.DeployGpo.GpoName)")) | out-null
            # inventory existing GPO's
            foreach ($mdiGpo in $mdiGpoList) {
                try {
                    if ($IdentityLocation.ToUpper() -eq 'DOMAIN') {
                        $guid=($myDomain.GetGpo("$($mdiGpo.name)",$null)).Properties["cn"].trimstart('{').trimend('}')
                    } else {
                        $guid=($myForest.GetGpo("$($mdiGpo.name)",$null)).Properties["cn"].trimstart('{').trimend('}')
                    }
                } catch {}
                if ($guid) {
                    $mdiGpoList.Find({param($x) $x.name -eq $mdiGpo.name}).guid = $guid
                }
                $guid = $null
            }
            $gposToRemove = $mdiGpoList.FindAll({param($x) $x.guid.length -gt 0})
            if ($gposToRemove) {
                $nameList = $gposToRemove.FindAll({param($x) $x.name -ne $null}).name
                Write-Output "Found the following GPO(s) to remove"
                Write-Output ($nameList -join "`r`n" | Out-String)
                if ((!($Force)) -and (!($WhatIf))) {
                    $title    = 'Confirm GPO Removal'
                    $question = 'Are you sure you want to remove these GPO(s)?'
                    $choices  = '&Yes', '&No'
                    $decision = $Host.UI.PromptForChoice($title, $question, $choices, 1)
                }
                foreach ($mdiGpo in $gposToRemove) {
                    if ($WhatIf) {
                        Write-Output "SIMULATING - Removal of GPO $($mdiGpo.name)"
                    } else {
                        if (($decision -eq 0) -or $Force) {
                            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "User was prompted for GPO removal and selected Yes OR Force was specified. Force is $Force. Removing the following GPO(s) $($nameList -join "," | Out-String)" -logSev "Info" | out-null
                            try {
                                if ($IdentityLocation.ToUpper() -eq 'DOMAIN') {
                                    $gpoRemove = $myDomain.RemoveGpo($null,$mdiGpo.guid)
                                } else {
                                    $gpoRemove = $myForest.RemoveGpo($null,$mdiGpo.guid)
                                }
                                if ($gpoRemove) {
                                    Write-Output "Successfully removed $($mdiGpo.name)"
                                } else {
                                    throw
                                }
                            } catch {
                                Write-Error "Failed to remove GPO $($mdiGpo.name) with GUID $($mdiGpo.guid). See $env:temp\mircatmdi.log for details"
                                Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to remove GPO $($mdiGpo.name) with GUID $($mdiGpo.guid). $($_.Exception)" -logSev "Error" | out-null
                            }
                        } else {
                            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "User was prompted for GPO removal and selected No. NOT removing the following GPO(s) $($nameList -join "," | Out-String)" -logSev "Info" | out-null
                        }
                    }
                    $gpoRemove = $false
                }
            }
        }
        if (Use-GpoName $Actions 'RemoveDeletedObjectsPermissions') {
            if ($WhatIf) {
                Write-Output "SIMULATING - Removing Deleted Objects container permissions for $Identity"
            } else {
                if (Set-MDIDeletedObjectsContainerPermission -RemovePermissions -Identity $Identity -IdentityLocation $IdentityLocation) {
                    Write-Output "Removing Deleted Objects container permissions for $Identity"
                } else {
                    Write-Error "Failed to remove Deleted Objects container permissions for $Identity. See $env:temp\mircatmdi.log for details"
                    Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to remove Deleted Objects container permissions for $Identity in $IdentityLocation. $($_.Exception)" -logSev "Error" | out-null
                }
            }
        }
        #remove identity and group (detect group)
        if (Use-GpoName $Actions 'RemoveIdentity') {
            $identityInfo = [PsCustomObject]@{
                exists = $false
                isGmsa = $false
                samAccountName = $null
                prinAllowedRetrievePass = $null
            }
            try {
                $foundIdentity = $mydomain.GetObjectByFilter("|(samaccountname=$identity)(samaccountname=$identity$)",$IdentityLocation)
                if ($foundIdentity) {
                    $identityInfo.exists = $true
                    $identityInfo.isGmsa = ("msDS-GroupManagedServiceAccount" -in $foundIdentity.properties.objectclass)
                    $identityInfo.samAccountName = ($foundIdentity.properties.samaccountname)
                    if ($identityInfo.isGmsa) {
                        $getAdServiceAccountParams = @{
                            Identity    = $Identity
                            Properties  = '*'
                            ErrorAction = 'SilentlyContinue'
                        }
                        if ($IdentityLocation.ToUpper() -eq 'DOMAIN') {
                            $getAdServiceAccountParams.Add('Server',$myDomain.chosenDc)
                            $localizedDcGroupName = $myDomain.privilegedGroupNames['Domain Controllers']
                        } else {
                            $getAdServiceAccountParams.Add('Server',$myForest.chosenDc)
                            $localizedDcGroupName = $myForest.privilegedGroupNames['Domain Controllers']
                        }
                        $identityInfo.prinAllowedRetrievePass = (Get-ADServiceAccount @getAdServiceAccountParams).PrincipalsAllowedToRetrieveManagedPassword | Where-Object  {($_ -notmatch $localizedDcGroupName) -and (!($_.StartsWith('S-1-5')))}
                    }
                } else {
                    throw
                }
            } catch {
                Write-Error "Failed to find identity $Identity in $IdentityLocation. See $env:temp\mircatmdi.log for details"
                Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to find identity $Identity in $IdentityLocation. $($_.Exception)" -logSev "Error" | out-null
            }

            if ($identityInfo.exists) {
                if ($null -ne $identityInfo.prinAllowedRetrievePass) {
                    Write-Output "Found the following groups(s) associated with $Identity"
                    Write-Output ($identityInfo.prinAllowedRetrievePass -join "`r`n" | Out-String)
                    foreach ($group in $identityInfo.prinAllowedRetrievePass) {
                        if (($null -ne $group) -and (-not [string]::IsNullOrEmpty($group))) {
                            if ((!($Force)) -and (!($WhatIf))) {
                                $title    = 'Confirm Group Removal'
                                $question = ('Are you sure you want to remove {0}?' -f $group)
                                $choices  = '&Yes', '&No'
                                $decision = $Host.UI.PromptForChoice($title, $question, $choices, 1)
                            }
                            if ($WhatIf) {
                                Write-Output "SIMULATING - Removing group $group"
                            } else {
                                if (($decision -eq 0) -or $Force) {
                                    try {
                                        if ($myDomain.RemoveObjectByDn($group, $IdentityLocation)) {
                                            Write-Output  "Removing group $group"
                                        } else {
                                            throw
                                        }
                                    } catch {
                                        Write-Error "Failed to remove $group in $IdentityLocation. See $env:temp\mircatmdi.log for details"
                                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to remove $group in $IdentityLocation. $($_.Exception)" -logSev "Error" | out-null
                                    }
                                }
                            }
                        }
                    }
                }
                if ((!($Force)) -and (!($WhatIf))) {
                    $title    = 'Confirm Identity Removal'
                    $question = ('Are you sure you want to remove {0}?' -f $Identity)
                    $choices  = '&Yes', '&No'
                    $decision = $Host.UI.PromptForChoice($title, $question, $choices, 1)
                }
                if ($WhatIf) {
                    Write-Output "SIMULATING - Removing Identity $Identity"
                } else {
                    if (($decision -eq 0) -or $Force) {
                        try {
                            $removeIdentityParams = @{
                                Identity    = $Identity
                            }
                            if ($IdentityLocation.ToUpper() -eq 'DOMAIN') {
                                $removeIdentityParams.Add('Server',$myDomain.chosenDc)
                            } else {
                                $removeIdentityParams.Add('Server',$myForest.chosenDc)
                            }
                            Write-Output  "Removing identity $Identity in $IdentityLocation"
                            if ($identityInfo.isGmsa) {
                                Remove-ADServiceAccount @removeIdentityParams -confirm:$false
                            } else {
                                Remove-ADUser @removeIdentityParams -confirm:$false
                            }
                            
                        } catch {
                            Write-Error "Failed to remove $Identity in $IdentityLocation. See $env:temp\mircatmdi.log for details"
                            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to remove $Identity in $IdentityLocation. $($_.Exception)" -logSev "Error" | out-null
                        }
                    }
                }
            }
        }
        
        #remove install folder
        if (Use-GpoName $Actions 'RemoveInstallationFolder') {
            if ($IdentityLocation.ToUpper() -eq 'DOMAIN') {
                $possibleInstallFolderPath = @(('{0}\{1}\{2}\{3}' -f $myDomain.writableSysvolPath,'scripts','mss','mdisetup'))
            } else {
                $possibleInstallFolderPath = @(('{0}\{1}\{2}\{3}' -f $myForest.writableSysvolPath,'scripts','mss','mdisetup'))
            }
            $foundPath = $false
            foreach ($installFolderPath in $possibleInstallFolderPath) {
                if (Test-Path $installFolderPath) {
                    $foundPath = $true
                    if ($WhatIf) {
                        Write-Output "SIMULATING - Removing folder $installFolderPath"
                    } else {
                        if ((!($Force)) -and (!($WhatIf))) {
                            $title    = 'Confirm Folder Removal'
                            $question = ('Are you sure you want to remove {0}?' -f $installFolderPath)
                            $choices  = '&Yes', '&No'
                            $decision = $Host.UI.PromptForChoice($title, $question, $choices, 1)
                        }
                        if (($decision -eq 0) -or $Force) {
                            Write-Output "Removing folder $installFolderPath"
                            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Removing folder $installFolderPath" -logSev "Info" | out-null
                            try {
                                Remove-Item -Path $installFolderPath -Recurse -Force -ErrorAction SilentlyContinue
                            } catch {
                                Write-Output "Failed to remove folder $installFolderPath"
                                Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to remove folder $installFolderPath. $($_.Exception)" -logSev "Error" | out-null
                            }
                            
                        }
                    }
                }
            }
            if (!($foundPath)) {
                Write-Warning "Unable to find installation folder at $($possibleInstallFolderPath -join ',' | out-string)Please delete manually"
                Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Unable to find installation folder at $($possibleInstallFolderPaths -join ',' | out-string)Please delete manually" -logSev "Warn" | out-null
            }
        }

        if (Use-GpoName $Actions 'CreateUninstallGPO') {
            if ($WhatIf) {
                Write-Output "SIMULATING - Creating GPO $($myMdiDomainReadiness.RemoveGpo.GpoName)"
            } else {
                Write-Output "Creating GPO $($myMdiDomainReadiness.RemoveGpo.GpoName)"
                New-MDIGpo -gposToCreate RemoveGpo -myDomain $myDomain -Force:$Force
            }
            
        }
                
    }
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
function Revoke-EntraAllUserRefreshTokens
{
  <#
  .SYNOPSIS
    Revokes refresh tokens for all or specified users in an Entra ID tenant.

  .DESCRIPTION
    This function revokes refresh tokens (sign-in sessions) for users in an Entra ID tenant 
    using Microsoft Graph API. It can target a specific user or all users in the tenant.
    The current user is automatically exempted from token revocation to prevent 
    disrupting the current session. Additional users can also be exempted.

  .PARAMETER userID
    The User Principal Name (UPN) of a specific user whose refresh tokens should be revoked.
    If not specified, the function will revoke tokens for all users in the tenant.

  .PARAMETER ExemptedUsers
    An array of User Principal Names (UPNs) or Object IDs that should be exempted from 
    token revocation. The current user is automatically added to this list.

  .EXAMPLE
    Revoke-EntraAllUserRefreshTokens
    Revokes refresh tokens for all users in the tenant, except the current user.

  .EXAMPLE
    Revoke-EntraAllUserRefreshTokens -userID "user@contoso.com"
    Revokes refresh tokens only for the specified user.

  .EXAMPLE
    Revoke-EntraAllUserRefreshTokens -ExemptedUsers @("admin@contoso.com", "critical-service@contoso.com")
    Revokes refresh tokens for all users except those specified and the current user.

  .NOTES
    This function requires sufficient permissions in Microsoft Graph API, 
    including User.RevokeSessions.All, Directory.ReadWrite.All, and User.ReadWrite.All.
  #>

  [CmdletBinding()]
  param (
    #For revoking a specific user. Provide UPN
    [Parameter()]
    [string]
    $userID,
    
    #Array of users to exempt from token revocation (UPN or Object ID)
    [Parameter()]
    [string[]]
    $ExemptedUsers
  )

  $graphAL = [GraphAL]::new(@{})

<#
  #region validateRequiredScopes
  #https://learn.microsoft.com/en-us/graph/api/user-revokesigninsessions?view=graph-rest-1.0&tabs=http
  $requiredScopes = @("User.RevokeSessions.All", "Directory.ReadWrite.All", "User.ReadWrite.All")
  $missingscopes = $graphAL.validateRequiredScopes(@{requiredScopes = $requiredScopes})
  If ($missingscopes -eq $true)
  {
    $s = $requiredScopes -join ", "
    Write-Error "Missing a required scope. Possibilities: $s"
    exit
  }
  #endregion validateRequiredScopes
#>

  #region Get current user info
  # Get current user to exempt them from token revocation
  $currentUser = $graphAL.invoke(@{
    method = "GET"
    uri = "me"
  })
  
  # Initialize exempted users array if not provided
  if (-not $ExemptedUsers) {
    $ExemptedUsers = @()
  }
  
  # Add current user to exemptions if not already included
  if ($currentUser.id -and -not $ExemptedUsers.Contains($currentUser.id) -and -not $ExemptedUsers.Contains($currentUser.userPrincipalName)) {
    $ExemptedUsers += $currentUser.id
  }
  #endregion Get current user info

  #region build users array
  $users = @()
  if ($userID)
  {
    $users += $userID
  } else {
    # Get all users Excluding B2B users
    #$tid = (Get-MgContext).TenantId
    $tid = $GraphALContext.tenantId
    #$uri = "https://graph.microsoft.com/v1.0/tenantRelationships/findTenantInformationByTenantId(tenantId='$tid')"
    $tenantName = $graphAL.invoke(@{
      method = "GET"
      uri = "tenantRelationships/findTenantInformationByTenantId(tenantId='$tid')"
    }).defaultDomainName
    
    #$tenantName = (Invoke-MgGraphRequest -Method GET -Uri $uri).defaultDomainName
    $b2bfilter = "%23EXT%23@$tenantName"
    #$uri = "https://graph.microsoft.com/v1.0/users?`$filter=not(endswith(userPrincipalName,'$b2bfilter'))&`$count=true"
    #$graphResults = (Invoke-MgGraphRequest -Method GET -Uri $uri -Headers @{ConsistencyLevel="eventual"} )
    $graphResults = $graphAL.invoke(@{
      method = "GET"
      uri = "users?`$filter=not(endswith(userPrincipalName,'$b2bfilter'))&`$count=true"
      headers = @{ConsistencyLevel="eventual"}
    })
    $users += $graphResults.value.id

    Write-Host "Getting users..." -ForegroundColor Cyan
  }
  #endregion build users array

  #region process exemptions
  # Remove exempted users from the list (by Object ID)
  $users = $users | Where-Object { $_ -notin $ExemptedUsers }
  
  # For UPN-based exemptions, we need to find and remove the corresponding user IDs
  $upnExemptions = $ExemptedUsers | Where-Object { $_ -like "*@*" }
  if ($upnExemptions.Count -gt 0) {
    $exemptedUserIds = @()
    
    foreach ($upn in $upnExemptions) {
      # Try to find the user by UPN
      try {
        $userResult = $graphAL.invoke(@{
          method = "GET"
          uri = "users?`$filter=userPrincipalName eq '$upn'"
        })
        
        if ($userResult.value.Count -gt 0) {
          $exemptedUserIds += $userResult.value.id
        }
      }
      catch {
        Write-Warning "Could not find user with UPN: $upn. Error: $_"
      }
    }
    
    # Remove users with matching IDs from the list
    if ($exemptedUserIds.Count -gt 0) {
      $users = $users | Where-Object { $_ -notin $exemptedUserIds }
    }
  }
  #endregion process exemptions

  #region revoke sessions
  Write-Host "Revoking sessions for $($users.Count) users..." -ForegroundColor Cyan
  
  Foreach ($user in $users)
  {
    try {
      # Revoke all user sessions
      $graphAL.invoke(@{
        method = "POST"
        uri = "users/$user/revokeSignInSessions"
      }) | Out-Null
      Write-Verbose "Successfully revoked sessions for user: $user"
    }
    catch {
      Write-Warning "Failed to revoke sessions for user: $user"
      Write-Warning "Error: $_"
      # Continue with the next user
    }
  }
  
  Write-Host "Token revocation complete." -ForegroundColor Green
  #endregion revoke sessions
}# end of function

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
function Set-AdRidPool {
    ##########################################################################################################
    <#
    .SYNOPSIS
        Raises and invalidates the RID pool in Active Directory. Creates a user account to verify operations
        and then deletes the account after verification.
    
    .DESCRIPTION
        Run Set-AdRidPool.

    .EXAMPLE
        Set-AdRidPool

    .OUTPUTS
        Writes to console

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
    Param(
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(Mandatory=$false)]
        [int64]$additionalValue=100000,
        [Parameter(DontShow)]
        $myDomain
    )
    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
    $raiseSuccess = $false
    $invalidateSuccess = $false
    try {
        # only invalidate if raise succeeds
        $existingRidPool = [int64](($myDomain.GetObjectByDn("CN=RID Manager$,CN=System,$($myDomain.domainDn)","domain")).Properties["ridavailablepool"])[0]
        $newRIDPool = $existingRidPool + $additionalValue
        $raiseSuccess = $myDomain.SetObjectByDn("CN=RID Manager$,CN=System,$($myDomain.domainDn)","rIDAvailablePool",[string]$newRIDPool,"domain")
        if ($raiseSuccess) {
            write-output "Raised RID pool from $existingRidPool to $newRidPool in domain $($myDomain.domainFqdn)"
        }
    } catch {
        $raiseSuccess = $false
        #make sure we didn't make the change
        if (!($existingRidPool -eq ($myDomain.GetObjectByDn("CN=RID Manager$,CN=System,$($myDomain.domainDn)","domain")).Properties["ridavailablepool"])) {
            try {
                $myDomain.SetObjectByDn("CN=RID Manager$,CN=System,$($myDomain.domainDn)","rIDAvailablePool",[string]$existingRidPool,"domain") | out-null
            } catch {}
        }
        write-error "Failed to raise RID pool in domain $($myDomain.domainFqdn)."
    }
    if ($raiseSuccess) {
        #invalidate rid
        try {
            $domainSid =  ($myDomain.GetObjectByDn("$($mydomain.domaindn)","domain").Properties["objectSid"])[0]
            $RootDSE = New-Object System.DirectoryServices.DirectoryEntry("LDAP://RootDSE")
            $RootDSE.UsePropertyCache = $false
            $RootDSE.Put("invalidateRidPool", $domainSid)
            $RootDSE.SetInfo()
            $invalidateSuccess = $true
            write-output "Invalidated RID pool in domain $($myDomain.domainFqdn)"
        } catch {
            write-error "Failed to invalidate RID pool in domain $($myDomain.domainFqdn). $($_.exception)"
        }
        if ($invalidateSuccess) {
            #create user (this will fail)
            start-sleep 5
            $newUser = $null
            $newUser = $myDomain.NewAdUser($($myDomain.domainDn),"RIDPoolTestUser","created to confirm RID Pool raise")
            start-sleep 5
            if ($newUser -eq $null) {
                write-output "Allocating new RID pool in domain $($myDomain.domainFqdn)."
                #create user (this should succeed)
                $newUser = $myDomain.NewAdUser($($myDomain.domainDn),"RIDPoolTestUser","created to confirm RID Pool raise")
                start-sleep 5
                #check for user is really created
                $userCheck = $myDomain.GetObjectByDn("CN=RIDPoolTestUser,$($mydomain.domaindn)","domain")
                $count = 0
                while (!($userCheck)) {
                    if ($count -eq 4) {
                        break
                    }
                    $userCheck = $myDomain.GetObjectByDn("CN=RIDPoolTestUser,$($mydomain.domaindn)","domain")
                    $count++
                }
                if ($userCheck) {
                    write-output "Allocated new RID pool in domain $($myDomain.domainFqdn)."
                }
                #delete user
                if ($myDomain.RemoveObjectByDn("CN=RIDPoolTestUser,$($mydomain.domaindn)","domain")) {
                    write-output "RID operations complete in domain $($myDomain.domainFqdn)."
                } else {
                    write-error "Failed to clean RID allocation in domain $($myDomain.domainFqdn). Please manually delete object at CN=RIDPoolTestUser,$($mydomain.domaindn)"
                }
            } else {
                write-error "Failed to allocate new RID pool in domain $($myDomain.domainFqdn)."
            }
        }
    } else {
        write-error "Failed to raise RID pool in domain $($myDomain.domainFqdn)."
    }
}
function Set-ClawDomainJoinDelegation {
    ##########################################################################################################
    <#
    .SYNOPSIS
        Delegate domain join ability at the specified OU to the specified group.
    
    .DESCRIPTION
        Delegate domain join ability at the specified OU to the specified group.

    .EXAMPLE
        Set-ClawDomainJoinDelegation

        Sets the delegation of control ACL's for joining the domain

    .OUTPUTS
        Verbose output to host.
        General log written to '$env:temp\claw.log'

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

    ###################################
    ## Function Options and Parameters
    ###################################

    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        #The target domain
        [Parameter(Mandatory=$false)]
        [string]$IDOUName="SITH",
        [Parameter(Mandatory=$false)]
        [string]$delegateTo="CLAW Domain Join",
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )

    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Entering function Set-ClawDomainJoinDelegation" -logSev "Info" | out-null
    write-verbose "Entering function Set-ClawDomainJoinDelegation"
    #Get domain object
    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
    $returnVal = $false
    
    try {
        $IDOUNamePath = Get-IDOUNamePath -IDOUName $IDOUName -myDomain $mydomain
        $ou = "OU=Staging,$IDOUNamePath"
        if (!($myDomain.SetAdAcl($ou,$delegateTo,"domain","CreateChild, DeleteChild","Allow","bf967a86-0de6-11d0-a285-00aa003049e2","All","00000000-0000-0000-0000-000000000000"))) {
            throw
        }
        if (!($myDomain.SetAdAcl($ou,$delegateTo,"domain","ReadProperty, WriteProperty","Allow","bf967a86-0de6-11d0-a285-00aa003049e2","Descendents","bf967a86-0de6-11d0-a285-00aa003049e2"))) {
            throw
        }
        if (!($myDomain.SetAdAcl($ou,$delegateTo,"domain","ReadProperty, WriteProperty","Allow","bf96793f-0de6-11d0-a285-00aa003049e2","Descendents","bf967a86-0de6-11d0-a285-00aa003049e2"))) {
            throw
        }
        if (!($myDomain.SetAdAcl($ou,$delegateTo,"domain","ReadProperty, WriteProperty","Allow","3e0abfd0-126a-11d0-a060-00aa006c33ed","Descendents","bf967a86-0de6-11d0-a285-00aa003049e2"))) {
            throw
        }
        if (!($myDomain.SetAdAcl($ou,$delegateTo,"domain","ReadProperty, WriteProperty","Allow","4c164200-20c0-11d0-a768-00aa006e0529","Descendents","bf967a86-0de6-11d0-a285-00aa003049e2"))) {
            throw
        }
        if (!($myDomain.SetAdAcl($ou,$delegateTo,"domain","WriteProperty","Allow","5f0a24d9-dffa-4cd9-acbf-a0680c03731e","Descendents","bf967a86-0de6-11d0-a285-00aa003049e2"))) {
            throw
        }
        if (!($myDomain.SetAdAcl($ou,$delegateTo,"domain","WriteProperty","Allow","fad5dcc1-2130-4c87-a118-75322cd67050","Descendents","bf967a86-0de6-11d0-a285-00aa003049e2"))) {
            throw
        }
        if (!($myDomain.SetAdAcl($ou,$delegateTo,"domain","WriteProperty","Allow","41bc7f04-be72-4930-bd10-1f3439412387","Descendents","bf967a86-0de6-11d0-a285-00aa003049e2"))) {
            throw
        }
        if (!($myDomain.SetAdAcl($ou,$delegateTo,"domain","ReadProperty","Allow","377ade80-e2d8-46c5-9bcd-6d9dec93b35e","Descendents","bf967a86-0de6-11d0-a285-00aa003049e2"))) {
            throw
        }
        if (!($myDomain.SetAdAcl($ou,$delegateTo,"domain","ReadProperty","Allow","8ce6a937-871b-4c92-b285-d99d4036681c","Descendents","bf967a86-0de6-11d0-a285-00aa003049e2"))) {
            throw
        }
        if (!($myDomain.SetAdAcl($ou,$delegateTo,"domain","Self","Allow","f3a64788-5306-11d1-a9c5-0000f80367c1","Descendents","bf967a86-0de6-11d0-a285-00aa003049e2"))) {
            throw
        }
        if (!($myDomain.SetAdAcl($ou,$delegateTo,"domain","Self","Allow","72e39547-7b18-11d1-adef-00c04fd8d5cd","Descendents","bf967a86-0de6-11d0-a285-00aa003049e2"))) {
            throw
        }
        if (!($myDomain.SetAdAcl($ou,$delegateTo,"domain","ExtendedRight","Allow","00299570-246d-11d0-a768-00aa006e0529","Descendents","bf967a86-0de6-11d0-a285-00aa003049e2"))) {
            throw
        }
        write-output "Created delegation of control ACL for domain join to $($myDomain.domainNetbiosName)\CLAW Domain Join group at OU=Staging,$IDOUNamePath."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Created delegation of control ACL for domain join to $($myDomain.domainNetbiosName)\CLAW Domain Join group at OU=Staging,$IDOUNamePath." -logSev "Info" | out-null
        $returnVal = $true
    } catch {
        Write-error "Failed to create delegation of control ACL for domain join to $($myDomain.domainNetbiosName)\CLAW Domain Join group at OU=Staging,$IDOUNamePath. See $env:temp\claw.log for details."
        $errorMessage = $_.Exception
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to create delegation of control ACL for domain join to $($myDomain.domainNetbiosName)\CLAW Domain Join group at OU=Staging,$IDOUNamePath. $errorMessage" -logSev "Error" | out-null
    }
    
}
function Set-ClawMachineAccountPasswordGpo
{
    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [parameter(Mandatory=$true)]
        $clawGpoGroups,
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [Parameter(Mandatory=$true)]
        $myDomain
    )

    try {
        $createdGpo.filePath.Add("tasks", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\Preferences\ScheduledTasks\ScheduledTasks.xml')
        # current time
        $currentTime = [datetime]::now
        # utc time
        $changedTime = $currentTime.ToUniversalTime()
        # start boundary, this is the repeated time for execution
        $startBoundary = $currentTime.AddHours(25)
        # expire time, this is the expiration date after 3 days
        $endBoundary = $currentTime.AddDays(3)
        $createdGpo.content.Add("tasks",('<?xml version="1.0" encoding="utf-8"?>{0}' -f [environment]::NewLine))
        $createdGpo.content["tasks"] += '<ScheduledTasks clsid="{CC63F200-7309-4ba0-B154-A71CD118DBCC}"><TaskV2 clsid="{D8896631-B747-47a7-84A6-C155337F3BC8}" name="Reset Machine Account Password" image="0" changed="'+$($changedTime.ToString("yyyy-MM-dd HH:mm:ss"))+'" uid="{D7FDACEC-FEE6-4173-994A-E56140817318}" userContext="0" removePolicy="0">'
        $createdGpo.content["tasks"] += '<Properties action="C" name="Reset Machine Account Password" runAs="NT AUTHORITY\System" logonType="S4U">'
        $createdGpo.content["tasks"] += '<Task version="1.2">'
        $createdGpo.content["tasks"] += '<RegistrationInfo>'
        $createdGpo.content["tasks"] += "<Author>$($myDomain.domainNetbiosName)\administrator</Author>"
        $createdGpo.content["tasks"] += '<Description></Description>'
        $createdGpo.content["tasks"] += '</RegistrationInfo>'
        $createdGpo.content["tasks"] += '<Principals>'
        $createdGpo.content["tasks"] += '<Principal id="Author">'
        $createdGpo.content["tasks"] += '<UserId>NT AUTHORITY\System</UserId>'
        $createdGpo.content["tasks"] += '<LogonType>S4U</LogonType>'
        $createdGpo.content["tasks"] += '<RunLevel>HighestAvailable</RunLevel>'
        $createdGpo.content["tasks"] += '</Principal>'
        $createdGpo.content["tasks"] += '</Principals>'
        $createdGpo.content["tasks"] += '<Settings>'
        $createdGpo.content["tasks"] += '<IdleSettings>'
        $createdGpo.content["tasks"] += '<Duration>PT10M</Duration>'
        $createdGpo.content["tasks"] += '<WaitTimeout>PT1H</WaitTimeout>'
        $createdGpo.content["tasks"] += '<StopOnIdleEnd>true</StopOnIdleEnd>'
        $createdGpo.content["tasks"] += '<RestartOnIdle>false</RestartOnIdle>'
        $createdGpo.content["tasks"] += '</IdleSettings>'
        $createdGpo.content["tasks"] += '<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>'
        $createdGpo.content["tasks"] += '<DisallowStartIfOnBatteries>true</DisallowStartIfOnBatteries>'
        $createdGpo.content["tasks"] += '<StopIfGoingOnBatteries>true</StopIfGoingOnBatteries>'
        $createdGpo.content["tasks"] += '<AllowHardTerminate>true</AllowHardTerminate>'
        $createdGpo.content["tasks"] += '<AllowStartOnDemand>true</AllowStartOnDemand>'
        $createdGpo.content["tasks"] += '<Enabled>true</Enabled>'
        $createdGpo.content["tasks"] += '<Hidden>false</Hidden>'
        $createdGpo.content["tasks"] += '<ExecutionTimeLimit>P3D</ExecutionTimeLimit>'
        $createdGpo.content["tasks"] += '<Priority>7</Priority>'
        $createdGpo.content["tasks"] += '</Settings>'
        $createdGpo.content["tasks"] += '<Triggers>'
        $createdGpo.content["tasks"] += '<RegistrationTrigger><Enabled>true</Enabled></RegistrationTrigger>'
        $createdGpo.content["tasks"] += '<CalendarTrigger>'
        $createdGpo.content["tasks"] += '<StartBoundary>'+$($startBoundary.ToString("yyyy-MM-ddTHH:mm:ss"))+'</StartBoundary>'
        $createdGpo.content["tasks"] += '<Enabled>true</Enabled>'
        $createdGpo.content["tasks"] += '<ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay><RandomDelay>PT1H</RandomDelay>'
        $createdGpo.content["tasks"] += '<EndBoundary>'+$($endBoundary.ToString("yyyy-MM-ddTHH:mm:ss"))+'</EndBoundary>'
        $createdGpo.content["tasks"] += '</CalendarTrigger>'
        $createdGpo.content["tasks"] += '</Triggers>'
        $createdGpo.content["tasks"] += '<Actions Context="Author">'
        $createdGpo.content["tasks"] += '<Exec><Command>powershell.exe</Command><Arguments>-command "&amp; {Set-ItemProperty -Path HKLM:\SYSTEM\CurrentControlSet\Services\Netlogon\Parameters -name MaximumPasswordAge -Value 1; Set-ItemProperty -Path HKLM:\SYSTEM\CurrentControlSet\Services\Netlogon\Parameters -name DisablePasswordChange -Value 0; restart-service netlogon; start-sleep 5; Set-ItemProperty -Path HKLM:\SYSTEM\CurrentControlSet\Services\Netlogon\Parameters -name MaximumPasswordAge -Value 30}"</Arguments></Exec>'
        $createdGpo.content["tasks"] += '</Actions>'
        $createdGpo.content["tasks"] += '</Task></Properties></TaskV2>'
        $createdGpo.content["tasks"] += '</ScheduledTasks>'
        try {
            Write-Output ("Setting content of GPO $($createdGpo.name)")
            if ($myDomain.SetGpoContent($createdGpo.filePath["tasks"],('{0}' -f $createdGpo.content["tasks"]),"UTF8")) {
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting content of GPO $($createdGpo.name) $($createdGpo.filePath) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
            } else {
                throw
            }
        } catch {
            $errorMessage = $_.Exception
            write-error "Failed to set content of GPO $($createdGpo.name) $($createdGpo.filePath["tasks"]) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set content of GPO $($createdGpo.name) $($createdGpo.filePath["tasks"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
        }
        try {
            if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("scheduledtasks"))) {
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
            } else {
                throw
            }
        } catch {
            $errorMessage = $_.Exception
            write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
        }
        try {
            if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
            } else {
                throw
            }
        } catch {
            $errorMessage = $_.Exception
            write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
        }
    } catch {
        # general failure here
        $errorMessage = $_.Exception
        write-error "Failed to create $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to create $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    
}
function Set-ClawT0BaselineAuditGpo {
    
    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [parameter(Mandatory=$true)]
        $clawGpoGroups,
        [Parameter(Mandatory=$true)]
        $myDomain
    )

    # dont need gpo header unless it's a standard gpo
    $createdGpo.filePath.Add("audit", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\microsoft\windows nt\Audit\audit.csv')
    $createdGpo.filePath.Add("registry", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\Preferences\Registry\Registry.xml')

    # build block for Tier 0 - Baseline Audit (Audit settings only)
    $createdGpo.content.Add("audit", 'Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value{0}' -f [environment]::NewLine)
    $createdGpo.content["audit"] += ',System,Audit Credential Validation,{0},Success,,1{1}' -f '{0cce923f-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Security Group Management,{0},Success,,1{1}' -f '{0cce9237-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Process Creation,{0},Success,,1{1}' -f '{0cce922b-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Security System Extension,{0},Success,,1' -f '{0cce9211-69ae-11d9-bed3-505054503030}'
    
    try {
        write-output ("Setting content of GPO $($createdGpo.name)")
        if ($myDomain.SetGpoContent($createdGpo.filePath["audit"],('{0}' -f $createdGpo.content["audit"]),"ascii")) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting AUDIT policy content of GPO $($createdGpo.name) $($createdGpo.filePath["audit"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set AUDIT policy content of GPO $($createdGpo.name) $($createdGpo.filePath["audit"]) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set AUDIT policy content of GPO $($createdGpo.name) $($createdGpo.filePath["audit"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    
    try {
        if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("audit"))) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }

    #add in registry settings
    if ($myDomain.credential -eq [System.Management.Automation.PSCredential]::Empty) {
        $gppSet = $false
        $gppParams = @{
            Guid      = $createdGpo.guid
            Type      = 'DWord'
            Key       = 'HKEY_LOCAL_MACHINE\Software\Policies\Microsoft\Windows\PowerShell\ScriptBlockLogging'
            ValueName = 'EnableScriptBlockLogging'
            Value     = 1
        }; if (-not [string]::IsNullOrEmpty($myDomain.chosenDc)) { $gppParams.Add("Server", $myDomain.chosenDc) }
        $gppParams2 = @{
            Guid      = $createdGpo.guid
            Type      = 'DWord'
            Key       = 'HKEY_LOCAL_MACHINE\Software\Policies\Microsoft\Windows\PowerShell\ScriptBlockLogging'
            ValueName = 'EnableScriptBlockInvocationLogging'
            Value     = 1
        }; if (-not [string]::IsNullOrEmpty($myDomain.chosenDc)) { $gppParams2.Add("Server", $myDomain.chosenDc) }
        try {
            $convertHash = ($gppParams | convertto-json | convertfrom-json)
            $convertHash2 = ($gppParams2 | convertto-json | convertfrom-json)
            $loggedParams = ($convertHash -join ',') + ($convertHash2 -join ',')
        } catch {}
        try {
            write-output ("Setting content of GPO $($createdGpo.name): Registry settings only")
            $gppSet = ([bool](Set-GPRegistryValue @gppParams -ErrorAction SilentlyContinue)) -and ([bool](Set-GPRegistryValue @gppParams2 -ErrorAction SilentlyContinue))
            if ($gppSet) {
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting policy content of GPO $($createdGpo.name) in domain $($myDomain.domainFqdn) using Set-GPRegistryValue $loggedParams." -logSev "Info" | out-null
            } else {
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set policy content of GPO $($createdGpo.name) in domain $($myDomain.domainFqdn) using Set-GPRegistryValue $loggedParams. $errorMessage" -logSev "Error" | out-null
                throw
            }
        } catch {
            $gppSet = $false
        }
    }
    if (($gppSet -eq $false) -or ($this.credential -ne [System.Management.Automation.PSCredential]::Empty)) {
        #build block for registry - PowerShell Script Block
        $createdGpo.content.Add("registry", '<?xml version="1.0" encoding="utf-8"?>{0}' -f [environment]::NewLine)
        $createdGpo.content["registry"] += '<RegistrySettings clsid="{A3CCFC41-DFDB-43a5-8D26-0FE8B954DA51}"><Registry clsid="{9CD4B2F4-923D-47f5-A062-E897DD1DAD50}" name="EnableScriptBlockLogging" status="EnableScriptBlockLogging" image="12" changed="2025-03-24 16:43:10" uid="{9C347846-B8AE-4EE3-AEE7-9E4610E1AE54}"><Properties action="U" displayDecimal="0" default="0" hive="HKEY_LOCAL_MACHINE" key="SOFTWARE\Policies\Microsoft\Windows\PowerShell\ScriptBlockLogging" name="EnableScriptBlockLogging" type="REG_DWORD" value="00000001"/></Registry>'+[environment]::NewLine
        $createdGpo.content["registry"] += '<Registry clsid="{9CD4B2F4-923D-47f5-A062-E897DD1DAD50}" name="EnableScriptBlockInvocationLogging" status="EnableScriptBlockInvocationLogging" image="12" changed="2025-03-24 16:44:08" uid="{B80195F6-A5AD-4933-9CF8-F3470CCAFD7E}"><Properties action="U" displayDecimal="0" default="0" hive="HKEY_LOCAL_MACHINE" key="SOFTWARE\Policies\Microsoft\Windows\PowerShell\ScriptBlockLogging" name="EnableScriptBlockInvocationLogging" type="REG_DWORD" value="00000001"/></Registry>'+[environment]::NewLine
        $createdGpo.content["registry"] += '</RegistrySettings>'
        try {
            Write-Output ("Setting content of GPO $($createdGpo.name): Registry settings only")
            if ($myDomain.SetGpoContent($createdGpo.filePath["registry"],('{0}' -f $createdGpo.content["registry"]),"UTF8")) {
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting GPP registry content of GPO $($createdGpo.name) $($createdGpo.filePath["registry"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
            } else {
                throw
            }
        } catch {
            $errorMessage = $_.Exception
            write-error "Failed to set GPP registry content of GPO $($createdGpo.name)in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set GPP registry content of GPO $($createdGpo.name) $($createdGpo.filePath["registry"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
        }
        try {
            if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("audit","registry"))) {
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
            } else {
                throw
            }
        } catch {
            $errorMessage = $_.Exception
            write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
        }
        try {
            if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
            } else {
                throw
            }
        } catch {
            $errorMessage = $_.Exception
            write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
        }
    }
}
function Set-ClawT0DisallowDSRMLoginGpo {
    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [parameter(Mandatory=$true)]
        $clawGpoGroups,
        [Parameter(Mandatory=$true)]
        $myDomain
    )

    # dont need gpo header unless it's a standard gpo
    $createdGpo.filePath.Add("registry", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\Preferences\Registry\Registry.xml')

    #build block for registry
    $createdGpo.content.Add("registry", '<?xml version="1.0" encoding="utf-8"?>{0}' -f [environment]::NewLine)
    $createdGpo.content["registry"] += '<RegistrySettings clsid="{A3CCFC41-DFDB-43a5-8D26-0FE8B954DA51}"><Registry clsid="{9CD4B2F4-923D-47f5-A062-E897DD1DAD50}" name="DsrmAdminLogonBehavior" status="DsrmAdminLogonBehavior" image="13" changed="2025-04-03 17:51:38" uid="{CF89AF27-8E6B-4778-BC51-9B9EEDDD09D4}" bypassErrors="1"><Properties action="D" displayDecimal="0" default="0" hive="HKEY_LOCAL_MACHINE" key="System\CurrentControlSet\Control\Lsa" name="DsrmAdminLogonBehavior" type="REG_DWORD" value="00000000"/><Filters><FilterRegistry bool="AND" not="0" type="VALUEEXISTS" hive="HKEY_LOCAL_MACHINE" key="System\CurrentControlSet\Control\Lsa" valueName="DsrmAdminLogonBehavior" valueType="REG_DWORD" valueData="00000000" displayDecimal="1" min="0.0.0.0" max="0.0.0.0" gte="1" lte="0"/></Filters></Registry>'+[environment]::NewLine
    $createdGpo.content["registry"] += '</RegistrySettings>'

    try {
        Write-Output ("Setting content of GPO $($createdGpo.name): Registry settings only")
        if ($myDomain.SetGpoContent($createdGpo.filePath["registry"],('{0}' -f $createdGpo.content["registry"]),"UTF8")) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting content of GPO $($createdGpo.name) $($createdGpo.filePath["registry"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set content of GPO $($createdGpo.name) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set content of GPO $($createdGpo.name) $($createdGpo.filePath["registry"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("registry"))) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
}
function Set-ClawT0DomainBlockGpo
{
    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [parameter(Mandatory=$true)]
        $clawGpoGroups,
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [Parameter(Mandatory=$true)]
        $myDomain
    )

    $gpoFileHeader = New-GpoHeader
    $createdGpo.filePath.Add("gpt", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\microsoft\windows nt\SecEdit\GptTmpl.inf')
    $createdGpo.content.Add("gpt", $gpoFileHeader)
    $createdGpo.content["gpt"] += [environment]::NewLine
    $createdGpo.content["gpt"] += '[Privilege Rights]{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyNetworkLogonRight = {0}{1}' -f ($clawGpoGroups["schemaAdmins"],$clawGpoGroups["enterpriseAdmins"],$clawGpoGroups["domainAcctOps"],$clawGpoGroups["domainAdmins"],$clawGpoGroups["domainAdministratorAccount"],$clawGpoGroups["domainBackupOps"],$clawGpoGroups["domainPrintOps"],$clawGpoGroups["domainServerOps"],$clawGpoGroups["tier0Operators"],$clawGpoGroups["tier0ServiceAccounts"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyRemoteInteractiveLogonRight = {0}{1}' -f ($clawGpoGroups["schemaAdmins"],$clawGpoGroups["enterpriseAdmins"],$clawGpoGroups["domainAcctOps"],$clawGpoGroups["domainAdmins"],$clawGpoGroups["domainAdministratorAccount"],$clawGpoGroups["domainBackupOps"],$clawGpoGroups["domainPrintOps"],$clawGpoGroups["domainServerOps"],$clawGpoGroups["tier0Operators"],$clawGpoGroups["tier0ServiceAccounts"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyBatchLogonRight = {0}{1}' -f ($clawGpoGroups["tier0ServiceAccounts"],$clawGpoGroups["domainAdmins"],$clawGpoGroups["domainAdministratorAccount"],$clawGpoGroups["enterpriseAdmins"],$clawGpoGroups["tier0Operators"],$clawGpoGroups["schemaAdmins"],$clawGpoGroups["domainPrintOps"],$clawGpoGroups["domainServerOps"],$clawGpoGroups["domainBackupOps"],$clawGpoGroups["domainAcctOps"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyServiceLogonRight = {0}{1}' -f ($clawGpoGroups["tier0ServiceAccounts"],$clawGpoGroups["domainAdmins"],$clawGpoGroups["domainAdministratorAccount"],$clawGpoGroups["enterpriseAdmins"],$clawGpoGroups["tier0Operators"],$clawGpoGroups["schemaAdmins"],$clawGpoGroups["domainPrintOps"],$clawGpoGroups["domainServerOps"],$clawGpoGroups["domainBackupOps"],$clawGpoGroups["domainAcctOps"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyInteractiveLogonRight = {0}{1}' -f ($clawGpoGroups["tier0ServiceAccounts"],$clawGpoGroups["domainAdmins"],$clawGpoGroups["domainBackupOps"],$clawGpoGroups["domainPrintOps"],$clawGpoGroups["enterpriseAdmins"],$clawGpoGroups["tier0Operators"],$clawGpoGroups["schemaAdmins"],$clawGpoGroups["domainServerOps"],$clawGpoGroups["domainAcctOps"] -ne $null -join ','),[environment]::NewLine
    try {
        Write-Output ("Setting content of GPO $($createdGpo.name)")
        if ($myDomain.SetGpoContent($createdGpo.filePath["gpt"],('{0}' -f $createdGpo.content["gpt"]),"unicode")) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("security"))) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        # last step is the GPO acls
        # this permission DENIES the apply gpo for Tier 0 - Computers
        write-output ("Setting ACL's on GPO $($createdGpo.name)")
        if (-not ($myDomain.GetObjectByName("Tier 0 Computers","DOMAIN",$false))) {
            write-warning "Tier 0 Computers group has not been found yet"
            start-sleep -seconds 20
        }
        if ($myDomain.AddGpoApplyAcl($($createdGpo.guid),"Tier 0 Computers","Deny","Domain")) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting ACL's on GPO for Tier 0 Computers on GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set ACL's for `'Tier 0 Computers`' on GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set ACL's for `'Tier 0 Computers`' on GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        $dcGroupName = $mydomain.GetObjectBySid("$($mydomain.domainsid)-516","domain").properties["samaccountname"]
        if ($myDomain.AddGpoApplyAcl($($createdGpo.guid),"$dcGroupName","Deny","Domain")) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting ACL's on GPO for $dcGroupName on GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        write-error "Failed to set ACL for Domain Controllers `'$dcGroupName`' on GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set ACL for Enterprise Read Only Domain Controllers on GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($enterpriseReadOnlyDomainControllers -ne $null) {
            if ($myDomain.AddGpoApplyAcl($($createdGpo.guid),"$($enterpriseReadOnlyDomainControllers.properties["samaccountname"])","Deny","Forest")) {
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting ACL's on GPO for $($enterpriseReadOnlyDomainControllers.properties["samaccountname"]) on GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
            } else {
                throw
            }
        }
    } catch {
        write-error "Failed to set ACL for Enterprise Read Only Domain Controllers `'$($enterpriseReadOnlyDomainControllers.properties["samaccountname"])`' on GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set ACL for Enterprise Read Only Domain Controllers `'$($enterpriseReadOnlyDomainControllers.properties["samaccountname"])`' on GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($readOnlyDomainControllers -ne $null) {
            if ($myDomain.AddGpoApplyAcl($($createdGpo.guid),"$($readOnlyDomainControllers.properties["samaccountname"])","Deny","Forest")) {
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting ACL's on GPO for $($readOnlyDomainControllers.properties["samaccountname"]) on GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
            } else {
                throw
            }
        }
    } catch {
        write-error "Failed to set ACL for Read-Only Domain Controllers `'$($readOnlyDomainControllers.properties["samaccountname"])`' on GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set ACL for Read-Only Domain Controllers `'$($readOnlyDomainControllers.properties["samaccountname"])`' on GPO $($createdGpo.name) $t0BlockGuid in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    ### new section for WMI filter
    # get the GUID if it's there
    try {
        $wmiFilterGuid = $myDomain.GetWmiFilter("Tier 0 - No DC Apply","domain")
        if ($wmiFilterGuid) {
            $wmiFilterGuid = $wmiFilterGuid.id.trim('}').trim('{')
            write-output "Found existing WMI Filter: Tier 0 - No DC Apply"
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Found existing WMI Filter: Tier 0 - No DC Apply. GUID: $wmiFilterGuid" -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $wmiFilterGuid = $myDomain.NewWmiFilter("Tier 0 - No DC Apply","Tier 0 - Used to prevent policy from applying to a domain controller","root\CIMv2",'Select * from Win32_ComputerSystem where DomainRole < 4')
        write-output 'Creating WMI Filter: Tier 0 - No DC Apply'
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage 'Creating WMI Filter: Tier 0 - No DC Apply' -logSev "Info" | out-null
    }
    #
    if ($wmiFilterGuid) {
        start-sleep 3
        try {
            $filterSetCheck = $myDomain.SetWmiFilter($createdGpo.guid,$wmiFilterGuid)
            if ($filterSetCheck) {
                write-output "Set filter to GPO: Tier 0 - Domain Block"
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Set filter to GPO: Tier 0 - Domain Block" -logSev "Info" | out-null
            } else {
                throw
            }
        }
        catch {
            write-error "Failed to set WMI filter to GPO: Tier 0 - Domain Block"
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set filter to GPO: Tier 0 - Domain Block. $($_.Exception)" -logSev "Error" | out-null
        }
    } else {
        write-error "Failed to create WMI filter: Tier 0 - No DC Apply"
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to create WMI filter: Tier 0 - No DC Apply" -logSev "Error" | out-null
    }
}
function Set-ClawT0DomainControllersGpo
{
    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [parameter(Mandatory=$true)]
        $clawGpoGroups,
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [Parameter(Mandatory=$true)]
        $myDomain
    )

    $gpoFileHeader = New-GpoHeader
    $createdGpo.filePath.Add("gpt", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\microsoft\windows nt\SecEdit\GptTmpl.inf')
    $createdGpo.filePath.Add("audit", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\microsoft\windows nt\Audit\audit.csv')
    $createdGpo.filePath.Add("registry", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\Preferences\Registry\Registry.xml')

    # build block for Tier 0 - Domain Controllers (Audit settings only)
    $createdGpo.content.Add("audit", 'Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value{0}' -f [environment]::NewLine)
    $createdGpo.content["audit"] += ',System,Audit Credential Validation,{0},Success and Failure,,3{1}' -f '{0cce923f-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Computer Account Management,{0},Success and Failure,,3{1}' -f '{0cce9236-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Distribution Group Management,{0},Success and Failure,,3{1}' -f '{0cce9238-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Other Account Management Events,{0},Success,,1{1}' -f '{0cce923a-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Security Group Management,{0},Success and Failure,,3{1}' -f '{0cce9237-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit User Account Management,{0},Success and Failure,,3{1}' -f '{0cce9235-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit PNP Activity,{0},Success,,1{1}' -f '{0cce9248-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Process Creation,{0},Success,,1{1}' -f '{0cce922b-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Directory Service Access,{0},Success and Failure,,3{1}' -f '{0cce923b-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Directory Service Changes,{0},Success and Failure,,3{1}' -f '{0cce923c-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Account Lockout,{0},Failure,,2{1}' -f '{0cce9217-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Group Membership,{0},Success,,1{1}' -f '{0cce9249-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Logon,{0},Success and Failure,,3{1}' -f '{0cce9215-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Other Logon/Logoff Events,{0},Success and Failure,,3{1}' -f '{0cce921c-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Special Logon,{0},Success,,1{1}' -f '{0cce921b-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Detailed File Share,{0},Failure,,2{1}' -f '{0cce9244-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit File Share,{0},Success and Failure,,3{1}' -f '{0cce9224-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Other Object Access Events,{0},Success and Failure,,3{1}' -f '{0cce9227-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Removable Storage,{0},Success and Failure,,3{1}' -f '{0cce9245-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Audit Policy Change,{0},Success,,1{1}' -f '{0cce922f-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Authentication Policy Change,{0},Success,,1{1}' -f '{0cce9230-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit MPSSVC Rule-Level Policy Change,{0},Success and Failure,,3{1}' -f '{0cce9232-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Other Policy Change Events,{0},Failure,,2{1}' -f '{0cce9234-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Sensitive Privilege Use,{0},Success and Failure,,3{1}' -f '{0cce9228-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Other System Events,{0},Success and Failure,,3{1}' -f '{0cce9214-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Security State Change,{0},Success,,1{1}' -f '{0cce9210-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Security System Extension,{0},Success and Failure,,3{1}' -f '{0cce9211-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit System Integrity,{0},Success and Failure,,3' -f '{0cce9212-69ae-11d9-bed3-505054503030}'

    # build block for Tier 0 - Domain Controllers (full policy)
    $createdGpo.content.Add("gpt", $gpoFileHeader)
    $createdGpo.content["gpt"] += [environment]::NewLine
    $createdGpo.content["gpt"] += '[System Access]{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'LSAAnonymousNameLookup = 0{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += '[Registry Values]{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Control\Lsa\MSV1_0\allownullsessionfallback=4,0{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\Software\Microsoft\Windows\CurrentVersion\Policies\System\InactivityTimeoutSecs=4,900{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\Software\Microsoft\Windows NT\CurrentVersion\Winlogon\ScRemoveOption=1,"1"{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Control\Lsa\SCENoApplyLegacyAuditPolicy=4,1{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Services\LDAP\LDAPClientIntegrity=4,1{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Control\Lsa\LmCompatibilityLevel=4,5{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Control\Lsa\MSV1_0\NTLMMinClientSec=4,537395200{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Services\Netlogon\Parameters\sealsecurechannel=4,1{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Control\Lsa\MSV1_0\NTLMMinServerSec=4,537395200{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Services\Netlogon\Parameters\requiresignorseal=4,1{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Services\Netlogon\Parameters\signsecurechannel=4,1{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Services\LanmanWorkstation\Parameters\RequireSecuritySignature=4,1{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Services\LanManServer\Parameters\requiresecuritysignature=4,1{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Services\Netlogon\Parameters\requirestrongkey=4,1{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Control\Lsa\RestrictAnonymousSAM=4,1{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Services\LanManServer\Parameters\RestrictNullSessAccess=4,1{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Control\Lsa\RestrictAnonymous=4,1{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Control\Session Manager\ProtectionMode=4,1{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Control\Lsa\LimitBlankPasswordUse=4,1{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Services\Netlogon\Parameters\maximumpasswordage=4,30{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Services\Netlogon\Parameters\disablepasswordchange=4,0{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Control\Lsa\NoLMHash=4,1{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Services\LanmanWorkstation\Parameters\EnablePlainTextPassword=4,0{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Services\NTDS\Parameters\LDAPServerIntegrity=4,2{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Services\Netlogon\Parameters\RefusePasswordChange=4,0{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Control\Lsa\MSV1_0\AuditReceivingNTLMTraffic=4,2{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Control\Lsa\MSV1_0\RestrictSendingNTLMTraffic=4,1{0}'  -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Services\Netlogon\Parameters\AuditNTLMInDomain=4,7{0}'  -f [environment]::NewLine
    $createdGpo.content["gpt"] += '[Privilege Rights]{0}' -f [environment]::NewLine
    # this needs Exchange Servers if present
    $createdGpo.content["gpt"] += 'SeSecurityPrivilege = {0}{1}' -f ($clawGpoGroups["localAdministrators"],$clawGpoGroups["exchangeServers"] -ne $null -join ','),[environment]::NewLine
    # 
    $createdGpo.content["gpt"] += 'SeCreateTokenPrivilege = {0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'SeTrustedCredManAccessPrivilege = {0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'SeRemoteInteractiveLogonRight = {0}{1}' -f $clawGpoGroups["localAdministrators"],[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeCreatePagefilePrivilege = {0}{1}' -f $clawGpoGroups["localAdministrators"],[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeRemoteShutdownPrivilege = {0}{1}' -f $clawGpoGroups["localAdministrators"],[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeLoadDriverPrivilege = {0}{1}' -f $clawGpoGroups["localAdministrators"],[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeRestorePrivilege = {0}{1}' -f $clawGpoGroups["localAdministrators"],[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeCreateGlobalPrivilege = {0}{1}' -f ($clawGpoGroups["ntNetService"],$clawGpoGroups["ntLocalService"],$clawGpoGroups["localAdministrators"],$clawGpoGroups["ntService"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeManageVolumePrivilege = {0}{1}' -f $clawGpoGroups["localAdministrators"],[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeInteractiveLogonRight = {0}{1}' -f $clawGpoGroups["localAdministrators"],[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeEnableDelegationPrivilege = {0}{1}' -f $clawGpoGroups["localAdministrators"],[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeCreatePermanentPrivilege = {0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDebugPrivilege = {0}{1}' -f $clawGpoGroups["localAdministrators"],[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeProfileSingleProcessPrivilege = {0}{1}' -f $clawGpoGroups["localAdministrators"],[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeBackupPrivilege = {0}{1}' -f $clawGpoGroups["localAdministrators"],[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeNetworkLogonRight = {0}{1}' -f ($clawGpoGroups["domainEntDcs"],$clawGpoGroups["domainAuthUsers"],$clawGpoGroups["localAdministrators"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeImpersonatePrivilege = {0}{1}' -f ($clawGpoGroups["ntNetService"],$clawGpoGroups["ntLocalService"],$clawGpoGroups["localAdministrators"],$clawGpoGroups["ntService"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeSystemEnvironmentPrivilege = {0}{1}' -f $clawGpoGroups["localAdministrators"],[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeLockMemoryPrivilege = {0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'SeTcbPrivilege = {0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'SeTakeOwnershipPrivilege = {0}{1}' -f $clawGpoGroups["localAdministrators"],[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyNetworkLogonRight = {0}{1}' -f ($clawGpoGroups["domainGuests"],$clawGpoGroups["domainGuestAccount"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyBatchLogonRight = {0}{1}' -f ($clawGpoGroups["domainGuests"],$clawGpoGroups["domainGuestAccount"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyRemoteInteractiveLogonRight = {0}{1}' -f ($clawGpoGroups["domainGuests"],$clawGpoGroups["domainGuestAccount"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyInteractiveLogonRight = {0}{1}' -f ($clawGpoGroups["domainGuests"],$clawGpoGroups["domainGuestAccount"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyServiceLogonRight = {0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'SeServiceLogonRight = {0}{1}' -f ($clawGpoGroups["ntAllServices"],$clawGpoGroups["tier0ServiceAccounts"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeBatchLogonRight = {0}{1}' -f ($clawGpoGroups["builtinPerformanceLogUsers"],$clawGpoGroups["domainBackupOps"],$clawGpoGroups["localAdministrators"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += '[Service General Setting]{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += '"AppIDSvc",2,""{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += '"Spooler",4,""{0}' -f [environment]::NewLine
    #build block for registry GPP
    $createdGpo.content.Add("registry", '<?xml version="1.0" encoding="utf-8"?>{0}' -f [environment]::NewLine)
    $createdGpo.content["registry"] += '<RegistrySettings clsid="{A3CCFC41-DFDB-43a5-8D26-0FE8B954DA51}"><Registry clsid="{9CD4B2F4-923D-47f5-A062-E897DD1DAD50}" name="AllowInsecureGuestAuth" status="AllowInsecureGuestAuth" image="12" changed="2024-07-15 17:30:13" uid="{7ECFC955-ED91-4510-96DC-0EAD748E1F97}"><Properties action="U" displayDecimal="0" default="0" hive="HKEY_LOCAL_MACHINE" key="SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters" name="AllowInsecureGuestAuth" type="REG_DWORD" value="00000000"/></Registry>'+[environment]::NewLine
    $createdGpo.content["registry"] += '</RegistrySettings>'
    try {
        write-output ("Setting content of GPO $($createdGpo.name): Audit policies only")
        # this is multipart, set audit first
        if ($myDomain.SetGpoContent($createdGpo.filePath["audit"],('{0}' -f $createdGpo.content["audit"]),"ascii")) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting AUDIT policy content of GPO $($createdGpo.name) $($createdGpo.filePath["audit"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set AUDIT policy content of GPO $($createdGpo.name) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set AUDIT policy content of GPO $($createdGpo.name) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        write-output ("Setting content of GPO $($createdGpo.name)")
        # now you set the GPO body
        if ($myDomain.SetGpoContent($createdGpo.filePath["gpt"],('{0}' -f $createdGpo.content["gpt"]),"unicode")) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set content of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set content of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        Write-Output ("Setting content of GPO $($createdGpo.name): Registry settings only")
        if ($myDomain.SetGpoContent($createdGpo.filePath["registry"],('{0}' -f $createdGpo.content["registry"]),"UTF8")) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting GPP registry content of GPO $($createdGpo.name) $($createdGpo.filePath["registry"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set GPP registry content of GPO $($createdGpo.name)in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set GPP registry content of GPO $($createdGpo.name) $($createdGpo.filePath["registry"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("security","registry","audit"))) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
}
function Set-ClawT0ESXAdminsRestrictedGroupGpo {

    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [parameter(Mandatory=$true)]
        $clawGpoGroups,
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [Parameter(Mandatory=$true)]
        $myDomain
    )

    $gpoFileHeader = New-GpoHeader
    $createdGpo.filePath.Add("gpt", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\microsoft\windows nt\SecEdit\GptTmpl.inf')
    # build block for Tier 0 - Restricted Groups
    $createdGpo.content.Add("gpt", $gpoFileHeader)
    $createdGpo.content["gpt"] += [environment]::NewLine
    $createdGpo.content["gpt"] += '[Group Membership]{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += '{0}__Memberof ={1}' -f $clawGpoGroups["esxAdmins"],[environment]::NewLine
    $createdGpo.content["gpt"] += '{0}__Members ={1}' -f $clawGpoGroups["esxAdmins"],[environment]::NewLine
    
    try {
        write-host ("Setting content of GPO $($createdGpo.name)")
        if ($myDomain.SetGpoContent($createdGpo.filePath["gpt"],('{0}' -f $createdGpo.content["gpt"]),"unicode")) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set content of GPO $($createdGpo.name) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("security"))) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
}
function Set-ClawT0RestrictedGroupsGpo
{
    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [parameter(Mandatory=$true)]
        $clawGpoGroups,
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [Parameter(Mandatory=$true)]
        $myDomain
    )

    $gpoFileHeader = New-GpoHeader
    $createdGpo.filePath.Add("gpt", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\microsoft\windows nt\SecEdit\GptTmpl.inf')
    # build block for Tier 0 - Restricted Groups
    $createdGpo.content.Add("gpt", $gpoFileHeader)
    $createdGpo.content["gpt"] += [environment]::NewLine
    $createdGpo.content["gpt"] += '[Group Membership]{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += '*S-1-5-32-544__Memberof ={0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += '*S-1-5-32-544__Members = {0},Administrator{1}' -f $clawGpoGroups["tier0Operators"],[environment]::NewLine

    try {
        write-host ("Setting content of GPO $($createdGpo.name)")
        if ($myDomain.SetGpoContent($createdGpo.filePath["gpt"],('{0}' -f $createdGpo.content["gpt"]),"unicode")) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set content of GPO $($createdGpo.name) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("security"))) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
}
function Set-ClawT0UserRightsGpo
{
    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [parameter(Mandatory=$true)]
        $clawGpoGroups,
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [Parameter(Mandatory=$true)]
        $myDomain
    )

    $gpoFileHeader = New-GpoHeader
    $createdGpo.filePath.Add("gpt", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\microsoft\windows nt\SecEdit\GptTmpl.inf')
    $createdGpo.filePath.Add("registry", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\Preferences\Registry\Registry.xml')
    #and now we need a GPP reg file too
    # build block for Tier 0 - User Rights Assignments
    $createdGpo.content.Add("gpt", $gpoFileHeader)
    $createdGpo.content["gpt"] += [environment]::NewLine
    $createdGpo.content["gpt"] += '[Privilege Rights]{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyNetworkLogonRight = {0}{1}' -f ($clawGpoGroups["tier2Operators"],$clawGpoGroups["tier1Operators"],$clawGpoGroups["localAccountAndAdmins"],$clawGpoGroups["domainGuestAccount"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyRemoteInteractiveLogonRight = {0}{1}' -f ($clawGpoGroups["localAccountAndAdmins"],$clawGpoGroups["tier2Operators"],$clawGpoGroups["tier1Operators"],$clawGpoGroups["domainAcctOps"],$clawGpoGroups["domainGuestAccount"],$clawGpoGroups["domainPrintOps"],$clawGpoGroups["domainServerOps"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyBatchLogonRight = {0}{1}' -f ($clawGpoGroups["tier2Operators"],$clawGpoGroups["tier1Operators"],$clawGpoGroups["schemaAdmins"],$clawGpoGroups["domainGuestAccount"],$clawGpoGroups["domainServerOps"],$clawGpoGroups["domainPrintOps"],$clawGpoGroups["domainAcctOps"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyServiceLogonRight = {0}{1}' -f ($clawGpoGroups["tier2Operators"],$clawGpoGroups["tier1Operators"],$clawGpoGroups["schemaAdmins"],$clawGpoGroups["domainGuestAccount"],$clawGpoGroups["domainServerOps"],$clawGpoGroups["domainPrintOps"],$clawGpoGroups["domainAcctOps"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyInteractiveLogonRight = {0}{1}' -f ($clawGpoGroups["tier2Operators"],$clawGpoGroups["tier1Operators"],$clawGpoGroups["domainAcctOps"],$clawGpoGroups["domainGuestAccount"],$clawGpoGroups["domainPrintOps"],$clawGpoGroups["domainServerOps"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeBatchLogonRight = {0}{1}' -f ($clawGpoGroups["builtinPerformanceLogUsers"],$clawgpoGroups["domainBackupOps"],$clawGpoGroups["localAdministrators"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeInteractiveLogonRight = {0}{1}' -f ($clawGpoGroups["tier0Operators"],$clawGpoGroups["localAdministrators"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeRemoteInteractiveLogonRight = {0}{1}' -f ($clawGpoGroups["localAdministrators"],$clawGpoGroups["tier0Operators"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeServiceLogonRight = {0}{1}' -f ($clawGpoGroups["ntAllServices"],$clawGpoGroups["tier0ServiceAccounts"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeNetworkLogonRight = {0}{1}' -f ($clawGpoGroups["domainAuthUsers"],$clawGpoGroups["localAdministrators"] -ne $null -join ','),[environment]::NewLine
    #build block for registry GPP
    $createdGpo.content.Add("registry", '<?xml version="1.0" encoding="utf-8"?>{0}' -f [environment]::NewLine)
    $createdGpo.content["registry"] += '<RegistrySettings clsid="{A3CCFC41-DFDB-43a5-8D26-0FE8B954DA51}"><Registry clsid="{9CD4B2F4-923D-47f5-A062-E897DD1DAD50}" name="AllowInsecureGuestAuth" status="AllowInsecureGuestAuth" image="12" changed="2024-07-15 17:30:13" uid="{7ECFC955-ED91-4510-96DC-0EAD748E1F97}"><Properties action="U" displayDecimal="0" default="0" hive="HKEY_LOCAL_MACHINE" key="SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters" name="AllowInsecureGuestAuth" type="REG_DWORD" value="00000000"/></Registry>'+[environment]::NewLine
    $createdGpo.content["registry"] += '</RegistrySettings>'
    try {
        write-output ("Setting content of $($createdGpo.name)")
        if ($myDomain.SetGpoContent($createdGpo.filePath["gpt"],('{0}' -f $createdGpo.content["gpt"]),"unicode")) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set content of GPO $($createdGpo.name) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn). $errormessage" -logSev "Error" | out-null
    }
    try {
        Write-Output ("Setting content of GPO $($createdGpo.name): Registry settings only")
        if ($myDomain.SetGpoContent($createdGpo.filePath["registry"],('{0}' -f $createdGpo.content["registry"]),"UTF8")) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting GPP registry content of GPO $($createdGpo.name) $($createdGpo.filePath["registry"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set GPP registry content of GPO $($createdGpo.name) $($createdGpo.filePath["registry"]) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set GPP registry content of GPO $($createdGpo.name) $($createdGpo.filePath["registry"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("security","registry"))) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
}
function Set-ClawT1LocalAdminSpliceGpo
{
    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [parameter(Mandatory=$true)]
        $clawGpoGroups,
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [Parameter(Mandatory=$true)]
        $myDomain
    )
    
    $createdGpo.filePath.Add("groups", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\Preferences\Groups\Groups.xml')
    try {
        #did we get sid of Tier 1 Operators
        if (!($clawGpoGroups["tier1Operators"])) {
            throw "Group not found or SID not translated"
        }
        #set the NAME format as DOMAIN\GROUP
        $t1OperatorsGroupName = '{0}\{1}' -f $myDomain.DomainNetBiosName,"Tier 1 Operators"
        #generate new GUID for uid param
        $t1ServerOpsGpoUid = '{'+$(new-guid).ToString().ToUpper()+'}'
        #get the date time and format as "2024-01-29 19:28:53"
        $t1ServerOpsGpoTime = $((Get-Date).ToString("yyyy-MM-dd HH:mm:ss"))
        $createdGpo.content.Add("groups", '<?xml version="1.0" encoding="utf-8"?>')
        $createdGpo.content["groups"] += '<Groups clsid="{3125E937-EB16-4b4c-9934-544FC6D24D26}"><Group clsid="{6D4A79E4-529C-4481-ABD0-F5BD7EA93BA7}" name="Administrators (built-in)" image="2" changed="'+$t1ServerOpsGpoTime+'" uid="'+$t1ServerOpsGpoUid+'"><Properties action="U" newName="" description="" deleteAllUsers="0" deleteAllGroups="0" removeAccounts="0" groupSid="S-1-5-32-544" groupName="Administrators (built-in)"><Members><Member name="'+$t1OperatorsGroupName+'" action="ADD" sid="'+$(($clawGpoGroups["tier1Operators"]).trimstart("*"))+'"/></Members></Properties></Group>'
        $createdGpo.content["groups"] += '</Groups>'
        try {
            Write-Output ("Setting content of GPO $($createdGpo.name)")
            if ($myDomain.SetGpoContent($createdGpo.filePath["groups"],('{0}' -f $createdGpo.content["groups"]),"UTF8")) {
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting content of GPO $($createdGpo.name) $($createdGpo.filePath["groups"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
            } else {
                throw
            }
        } catch {
            $errorMessage = $_.Exception
            write-error "Failed to set content of GPO $($createdGpo.name) $($createdGpo.filePath["groups"]) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set content of GPO $($createdGpo.name) $($createdGpo.filePath["groups"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
        }
        try {
            if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("localusersandgroups"))) {
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
            } else {
                throw
            }
        } catch {
            $errorMessage = $_.Exception
            write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
        }
        try {
            if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
            } else {
                throw
            }
        } catch {
            $errorMessage = $_.Exception
            write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
        }
    }
    catch {
        # general failure here
        $errorMessage = $_.Exception
        write-error "Failed to retrieve group information needed to create $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to retrieve group information needed to create $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
}
function Set-ClawT1RestrictedGroupsGpo
{
    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [parameter(Mandatory=$true)]
        $clawGpoGroups,
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [Parameter(Mandatory=$true)]
        $myDomain
    )

    $gpoFileHeader = New-GpoHeader
    $createdGpo.filePath.Add("gpt", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\microsoft\windows nt\SecEdit\GptTmpl.inf')
    # build block for Tier 1 - Restricted Groups
    $createdGpo.content.Add("gpt", $gpoFileHeader)
    $createdGpo.content["gpt"] += [environment]::NewLine
    $createdGpo.content["gpt"] += '[Group Membership]{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += '*S-1-5-32-544__Memberof ={0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += '*S-1-5-32-544__Members = {0},Administrator{1}' -f $clawGpoGroups["tier1Operators"],[environment]::NewLine

    try {
        write-host ("Setting content of GPO $($createdGpo.name)")
        if ($myDomain.SetGpoContent($createdGpo.filePath["gpt"],('{0}' -f $createdGpo.content["gpt"]),"unicode")) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set content of GPO $($createdGpo.name) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("security"))) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
}
function Set-ClawT1UserRightsGpo
{
    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [parameter(Mandatory=$true)]
        $clawGpoGroups,
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [Parameter(Mandatory=$true)]
        $myDomain
    )

    $gpoFileHeader = New-GpoHeader
    $createdGpo.filePath.Add("gpt", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\microsoft\windows nt\SecEdit\GptTmpl.inf')
    # build block for Tier 1 - User Rights Assignments
    $createdGpo.content.Add("gpt", $gpoFileHeader)
    $createdGpo.content["gpt"] += [environment]::NewLine
    $createdGpo.content["gpt"] += '[Privilege Rights]{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyNetworkLogonRight = {0}{1}' -f ($clawGpoGroups["schemaAdmins"],$clawGpoGroups["enterpriseAdmins"],$clawGpoGroups["domainAcctOps"],$clawGpoGroups["domainAdmins"],$clawGpoGroups["domainAdministratorAccount"],$clawGpoGroups["domainBackupOps"],$clawGpoGroups["domainPrintOps"],$clawGpoGroups["domainServerOps"],$clawGpoGroups["tier0Operators"],$clawGpoGroups["tier0ServiceAccounts"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyRemoteInteractiveLogonRight = {0}{1}' -f ($clawGpoGroups["schemaAdmins"],$clawGpoGroups["enterpriseAdmins"],$clawGpoGroups["domainAcctOps"],$clawGpoGroups["domainAdmins"],$clawGpoGroups["domainAdministratorAccount"],$clawGpoGroups["domainBackupOps"],$clawGpoGroups["domainPrintOps"],$clawGpoGroups["domainServerOps"],$clawGpoGroups["tier0Operators"],$clawGpoGroups["tier0ServiceAccounts"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyBatchLogonRight = {0}{1}' -f ($clawGpoGroups["tier0ServiceAccounts"],$clawGpoGroups["domainAdmins"],$clawGpoGroups["domainAdministratorAccount"],$clawGpoGroups["enterpriseAdmins"],$clawGpoGroups["tier0Operators"],$clawGpoGroups["schemaAdmins"],$clawGpoGroups["domainPrintOps"],$clawGpoGroups["domainServerOps"],$clawGpoGroups["domainBackupOps"],$clawGpoGroups["domainAcctOps"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyServiceLogonRight = {0}{1}' -f ($clawGpoGroups["tier0ServiceAccounts"],$clawGpoGroups["domainAdmins"],$clawGpoGroups["domainAdministratorAccount"],$clawGpoGroups["enterpriseAdmins"],$clawGpoGroups["tier0Operators"],$clawGpoGroups["schemaAdmins"],$clawGpoGroups["domainPrintOps"],$clawGpoGroups["domainServerOps"],$clawGpoGroups["domainBackupOps"],$clawGpoGroups["domainAcctOps"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyInteractiveLogonRight = {0}{1}' -f ($clawGpoGroups["tier0ServiceAccounts"],$clawGpoGroups["domainAdmins"],$clawGpoGroups["domainBackupOps"],$clawGpoGroups["domainPrintOps"],$clawGpoGroups["enterpriseAdmins"],$clawGpoGroups["tier0Operators"],$clawGpoGroups["schemaAdmins"],$clawGpoGroups["domainServerOps"],$clawGpoGroups["domainAcctOps"] -ne $null -join ','),[environment]::NewLine
    
    $createdGpo.content["gpt"] += 'SeInteractiveLogonRight = {0}{1}' -f ($clawGpoGroups["localAdministrators"]),[environment]::NewLine
    try {
        write-output ("Setting content of $($createdGpo.name)")
        if ($myDomain.SetGpoContent($createdGpo.filePath["gpt"],('{0}' -f $createdGpo.content["gpt"]),"unicode")) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set content of GPO $($createdGpo.name) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn). $errormessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("security"))) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
}
function Set-ClawT2LocalAdminSpliceGpo
{
    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [parameter(Mandatory=$true)]
        $clawGpoGroups,
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [Parameter(Mandatory=$true)]
        $myDomain
    )
    
    $createdGpo.filePath.Add("groups", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\Preferences\Groups\Groups.xml')
    try {
        #did we get sid of Tier 2 Operators
        if (!($clawGpoGroups["tier2Operators"])) {
            throw "Group not found or SID not translated"
        }
        #set the NAME format as DOMAIN\GROUP
        $t2OperatorsGroupName = '{0}\{1}' -f $myDomain.DomainNetBiosName,"Tier 2 Operators"
        #generate new GUID for uid param
        $t2ServerOpsGpoUid = '{'+$(new-guid).ToString().ToUpper()+'}'
        #get the date time and format as "2024-01-29 19:28:53"
        $t2ServerOpsGpoTime = $((Get-Date).ToString("yyyy-MM-dd HH:mm:ss"))
        $createdGpo.content.Add("groups", '<?xml version="1.0" encoding="utf-8"?>')
        $createdGpo.content["groups"] += '<Groups clsid="{3125E937-EB16-4b4c-9934-544FC6D24D26}"><Group clsid="{6D4A79E4-529C-4481-ABD0-F5BD7EA93BA7}" name="Administrators (built-in)" image="2" changed="'+$t2ServerOpsGpoTime+'" uid="'+$t2ServerOpsGpoUid+'"><Properties action="U" newName="" description="" deleteAllUsers="0" deleteAllGroups="0" removeAccounts="0" groupSid="S-1-5-32-544" groupName="Administrators (built-in)"><Members><Member name="'+$t2OperatorsGroupName+'" action="ADD" sid="'+$(($clawGpoGroups["tier2Operators"]).trimstart("*"))+'"/></Members></Properties></Group>'
        $createdGpo.content["groups"] += '</Groups>'
        try {
            Write-Output ("Setting content of GPO $($createdGpo.name)")
            if ($myDomain.SetGpoContent($createdGpo.filePath["groups"],('{0}' -f $createdGpo.content["groups"]),"UTF8")) {
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting content of GPO $($createdGpo.name) $($createdGpo.filePath["groups"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
            } else {
                throw
            }
        } catch {
            $errorMessage = $_.Exception
            write-error "Failed to set content of GPO $($createdGpo.name) $($createdGpo.filePath["groups"]) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set content of GPO $($createdGpo.name) $($createdGpo.filePath["groups"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
        }
        try {
            if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("localusersandgroups"))) {
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
            } else {
                throw
            }
        } catch {
            $errorMessage = $_.Exception
            write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
        }
        try {
            if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
            } else {
                throw
            }
        } catch {
            $errorMessage = $_.Exception
            write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
        }
    }
    catch {
        # general failure here
        $errorMessage = $_.Exception
        write-error "Failed to retrieve group information needed to create $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to retrieve group information needed to create $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
}
function Set-ClawT2RestrictedGroupsGpo
{
    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [parameter(Mandatory=$true)]
        $clawGpoGroups,
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [Parameter(Mandatory=$true)]
        $myDomain
    )

    $gpoFileHeader = New-GpoHeader
    $createdGpo.filePath.Add("gpt", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\microsoft\windows nt\SecEdit\GptTmpl.inf')
    # build block for Tier 2 - Restricted Groups
    $createdGpo.content.Add("gpt", $gpoFileHeader)
    $createdGpo.content["gpt"] += [environment]::NewLine
    $createdGpo.content["gpt"] += '[Group Membership]{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += '*S-1-5-32-544__Memberof ={0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += '*S-1-5-32-544__Members = {0},Administrator{1}' -f $clawGpoGroups["tier2Operators"],[environment]::NewLine

    try {
        write-host ("Setting content of GPO $($createdGpo.name)")
        if ($myDomain.SetGpoContent($createdGpo.filePath["gpt"],('{0}' -f $createdGpo.content["gpt"]),"unicode")) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set content of GPO $($createdGpo.name) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("security"))) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
}
function Set-ClawT2UserRightsGpo
{
    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [parameter(Mandatory=$true)]
        $clawGpoGroups,
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [Parameter(Mandatory=$true)]
        $myDomain
    )

    $gpoFileHeader = New-GpoHeader
    $createdGpo.filePath.Add("gpt", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\microsoft\windows nt\SecEdit\GptTmpl.inf')
    # build block for Tier 2 - User Rights Assignments
    $createdGpo.content.Add("gpt", $gpoFileHeader)
    $createdGpo.content["gpt"] += [environment]::NewLine
    $createdGpo.content["gpt"] += '[Privilege Rights]{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyNetworkLogonRight = {0}{1}' -f ($clawGpoGroups["schemaAdmins"],$clawGpoGroups["enterpriseAdmins"],$clawGpoGroups["domainAcctOps"],$clawGpoGroups["domainAdmins"],$clawGpoGroups["domainAdministratorAccount"],$clawGpoGroups["domainBackupOps"],$clawGpoGroups["domainPrintOps"],$clawGpoGroups["domainServerOps"],$clawGpoGroups["tier0Operators"],$clawGpoGroups["tier0ServiceAccounts"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyRemoteInteractiveLogonRight = {0}{1}' -f ($clawGpoGroups["schemaAdmins"],$clawGpoGroups["enterpriseAdmins"],$clawGpoGroups["domainAcctOps"],$clawGpoGroups["domainAdmins"],$clawGpoGroups["domainAdministratorAccount"],$clawGpoGroups["domainBackupOps"],$clawGpoGroups["domainPrintOps"],$clawGpoGroups["domainServerOps"],$clawGpoGroups["tier0Operators"],$clawGpoGroups["tier0ServiceAccounts"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyBatchLogonRight = {0}{1}' -f ($clawGpoGroups["tier0ServiceAccounts"],$clawGpoGroups["domainAdmins"],$clawGpoGroups["domainAdministratorAccount"],$clawGpoGroups["enterpriseAdmins"],$clawGpoGroups["tier0Operators"],$clawGpoGroups["schemaAdmins"],$clawGpoGroups["domainPrintOps"],$clawGpoGroups["domainServerOps"],$clawGpoGroups["domainBackupOps"],$clawGpoGroups["domainAcctOps"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyServiceLogonRight = {0}{1}' -f ($clawGpoGroups["tier0ServiceAccounts"],$clawGpoGroups["domainAdmins"],$clawGpoGroups["domainAdministratorAccount"],$clawGpoGroups["enterpriseAdmins"],$clawGpoGroups["tier0Operators"],$clawGpoGroups["schemaAdmins"],$clawGpoGroups["domainPrintOps"],$clawGpoGroups["domainServerOps"],$clawGpoGroups["domainBackupOps"],$clawGpoGroups["domainAcctOps"] -ne $null -join ','),[environment]::NewLine
    $createdGpo.content["gpt"] += 'SeDenyInteractiveLogonRight = {0}{1}' -f ($clawGpoGroups["tier0ServiceAccounts"],$clawGpoGroups["domainAdmins"],$clawGpoGroups["domainBackupOps"],$clawGpoGroups["domainPrintOps"],$clawGpoGroups["enterpriseAdmins"],$clawGpoGroups["tier0Operators"],$clawGpoGroups["schemaAdmins"],$clawGpoGroups["domainServerOps"],$clawGpoGroups["domainAcctOps"] -ne $null -join ','),[environment]::NewLine
    
    $createdGpo.content["gpt"] += 'SeInteractiveLogonRight = {0}{1}' -f ($clawGpoGroups["localUsers"],$clawGpoGroups["localAdministrators"] -ne $null -join ','),[environment]::NewLine
    try {
        write-output ("Setting content of $($createdGpo.name)")
        if ($myDomain.SetGpoContent($createdGpo.filePath["gpt"],('{0}' -f $createdGpo.content["gpt"]),"unicode")) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set content of GPO $($createdGpo.name) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn). $errormessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("security"))) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
}
function Set-ClawTAllDisableSMB1Gpo {

#Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [parameter(Mandatory=$true)]
        $clawGpoGroups,
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [Parameter(Mandatory=$true)]
        $myDomain
    )

    $createdGpo.filePath.Add("registry", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\Preferences\Registry\Registry.xml')
    #build block for tier all smbv1
    $createdGpo.content.Add("registry", '<?xml version="1.0" encoding="utf-8"?>{0}' -f [environment]::NewLine)
    $createdGpo.content["registry"] += '<RegistrySettings clsid="{A3CCFC41-DFDB-43a5-8D26-0FE8B954DA51}"><Registry clsid="{9CD4B2F4-923D-47f5-A062-E897DD1DAD50}" name="SMB1" status="SMB1" image="12" changed="2024-07-24 16:48:02" uid="{4C46EAE9-2CFC-41C0-8C2C-A34F76CAFCAF}"><Properties action="U" displayDecimal="0" default="0" hive="HKEY_LOCAL_MACHINE" key="SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters" name="SMB1" type="REG_DWORD" value="00000000"/></Registry>'
    $createdGpo.content["registry"] += '</RegistrySettings>'
    # set the registry policy for disable smbv1
    try {
        Write-Output ("Setting content of GPO $($createdGpo.name)")
        if ($myDomain.SetGpoContent($createdGpo.filePath["registry"],('{0}' -f $createdGpo.content["registry"]),"UTF8")) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting content of GPO $($createdGpo.name) $($createdGpo.filePath["registry"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set content of GPO $($createdGpo.name) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set content of GPO $($createdGpo.name) $($createdGpo.filePath["registry"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("registry"))) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
}
function Set-TAllDisableWdigestGpo
{
    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [parameter(Mandatory=$true)]
        $clawGpoGroups,
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [Parameter(Mandatory=$true)]
        $myDomain
    )

    $createdGpo.filePath.Add("registry", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\Preferences\Registry\Registry.xml')
    #build block for tier all wdigest
    $createdGpo.content.Add("registry", '<?xml version="1.0" encoding="utf-8"?>{0}' -f [environment]::NewLine)
    $createdGpo.content["registry"] += '<RegistrySettings clsid="{A3CCFC41-DFDB-43a5-8D26-0FE8B954DA51}"><Registry clsid="{9CD4B2F4-923D-47f5-A062-E897DD1DAD50}" name="UseLogonCredential" status="UseLogonCredential" image="12" changed="2023-03-20 16:24:27" uid="{932DD2D1-CA47-48E7-AC0D-46738FC9CC54}"><Properties action="U" displayDecimal="0" default="0" hive="HKEY_LOCAL_MACHINE" key="SYSTEM\CurrentControlSet\Control\SecurityProviders\WDigest" name="UseLogonCredential" type="REG_DWORD" value="00000000"/></Registry>'
    $createdGpo.content["registry"] += '</RegistrySettings>'
    # set the registry policy for disable wdigest
    try {
        Write-Output ("Setting content of GPO $($createdGpo.name)")
        if ($myDomain.SetGpoContent($createdGpo.filePath["registry"],('{0}' -f $createdGpo.content["registry"]),"UTF8")) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting content of GPO $($createdGpo.name) $($createdGpo.filePath["registry"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set content of GPO $($createdGpo.name) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set content of GPO $($createdGpo.name) $($createdGpo.filePath["registry"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("registry"))) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\claw.log for details."
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
}
function Set-MDIAdfsAuditPermissions {
        ##########################################################################################################
    <#
    .SYNOPSIS
        Sets required auditing at the AD level for MDI to pick up event 4662
    
    .DESCRIPTION
        Audits for Everyone performing the actions CreateChild, DeleteChild, Self, WriteProperty, DeleteTree, ExtendedRight, Delete, WriteDacl, WriteOwner on users, groups, computers, and managed service accounts.

    .EXAMPLE
        Set-MDIAdfsAuditPermissions

    .OUTPUTS
        None.

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
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )
    $returnVal = $false
    #Get domain object
    if ($myDomain -eq $null) {
        try {
            if ($domain) {
                $myDomain = Get-MyDomain -domain $domain
            } else {
                $myDomain = Get-MyDomain
            }
            if (!($myDomain)) {
                throw
            }
        } catch {
            write-error "Failed to discover AD."
        	throw
        }
    } else {
        
    }
    
    if (([System.DirectoryServices.DirectoryEntry]::Exists('LDAP://CN=ADFS,CN=Microsoft,CN=Program Data,{0}' -f $myDomain.domainDn))) {
        try {
            $adfsPath = 'CN=ADFS,CN=Microsoft,CN=Program Data,{0}' -f $myDomain.domainDn
            $user = [Security.principal.NTAccount]"Everyone"
            #Descendant users bf967aba-0de6-11d0-a285-00aa003049e2
            #Descendant comps bf967a86-0de6-11d0-a285-00aa003049e2
            #Descendant groups bf967a9c-0de6-11d0-a285-00aa003049e2
            #Msds-managedserviceaccounts ce206244-5827-4a86-ba1c-1c0c386c1b64
            #Msds-groupmanagedserviceaccounts 7b8b558a-93a5-4af7-adca-c017e67f1057
            $guids=@("00000000-0000-0000-0000-000000000000")
            $acl = ($acl = get-acl -path "AD:\$adfsPath" -Audit)
            $rights=@("ReadProperty, WriteProperty")
            $auditFlags=@("Success","Failure")
            $inheritanceType="All"
            foreach ($guid in $guids) {
                foreach ($auditFlag in $auditFlags) {
                    $modified=$false
                    $auditRuleObject = New-Object System.DirectoryServices.ActiveDirectoryAuditRule($user,$rights,$auditFlag, $inheritanceType,[guid]$guid)
                    $acl.AddAuditRule($AuditRuleObject)
                }
            }
            set-acl "AD:\$adfsPath" $acl
            $returnVal = $true
        } catch {
            $errorMessage = $_.Exception
            write-error "$server`: Failed to set MDI ADFS Audit permissions"
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$server`: Failed to set MDI ADFS Audit permissions. $errorMessage" -logSev "Error" | out-null
            $returnVal = $false
        }
    }
    
    return $returnVal
}
function Set-MdiAuditAdcsGpo {
    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [Parameter(Mandatory=$true)]
        $myDomain
    )
    #we don't need the gpo header unless it's a standard GPO

    $createdGpo.filePath.Add("audit", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\microsoft\windows nt\Audit\audit.csv')

    # build block for MDI-All Audit Policies (Audit settings only)
    $createdGpo.content.Add("audit", 'Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value{0}' -f [environment]::NewLine)
    $createdGpo.content["audit"] += ',System,Audit Certification Services,{0cce9221-69ae-11d9-bed3-505054503030},Success and Failure,,3'

    try {
        write-output ("Setting content of GPO $($createdGpo.name)")
        if ($myDomain.SetGpoContent($createdGpo.filePath["audit"],('{0}' -f $createdGpo.content["audit"]),"ascii")) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting AUDIT policy content of GPO $($createdGpo.name) $($createdGpo.filePath["audit"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set AUDIT policy content of GPO $($createdGpo.name) $($createdGpo.filePath["audit"]) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set AUDIT policy content of GPO $($createdGpo.name) $($createdGpo.filePath["audit"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("audit"))) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
}
function Set-MdiAuditDcGpo {
    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [Parameter(Mandatory=$true)]
        $myDomain
    )
    $gpoFileHeader = New-GpoHeader

    $createdGpo.filePath.Add("gpt", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\microsoft\windows nt\SecEdit\GptTmpl.inf')
    $createdGpo.filePath.Add("audit", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\microsoft\windows nt\Audit\audit.csv')

    # build block for MDI-All Audit Policies (Audit settings only)
    $createdGpo.content.Add("audit", 'Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value{0}' -f [environment]::NewLine)
    $createdGpo.content["audit"] += ',System,Audit Credential Validation,{0},Success and Failure,,3{1}' -f '{0cce923f-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Computer Account Management,{0},Success and Failure,,3{1}' -f '{0cce9236-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Distribution Group Management,{0},Success and Failure,,3{1}' -f '{0cce9238-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Security Group Management,{0},Success and Failure,,3{1}' -f '{0cce9237-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit User Account Management,{0},Success and Failure,,3{1}' -f '{0cce9235-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Directory Service Access,{0},Success and Failure,,3{1}' -f '{0cce923b-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Directory Service Changes,{0},Success and Failure,,3{1}' -f '{0cce923c-69ae-11d9-bed3-505054503030}',[environment]::NewLine
    $createdGpo.content["audit"] += ',System,Audit Security System Extension,{0},Success and Failure,,3' -f '{0cce9211-69ae-11d9-bed3-505054503030}'

    $createdGpo.content.Add("gpt", $gpoFileHeader)
    $createdGpo.content["gpt"] += [environment]::NewLine
    $createdGpo.content["gpt"] += '[Registry Values]{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Control\Lsa\SCENoApplyLegacyAuditPolicy=4,1{0}'  -f [environment]::NewLine

    try {
        write-output ("Setting audit content of GPO $($createdGpo.name)")
        # this is multipart, set audit first
        if ($myDomain.SetGpoContent($createdGpo.filePath["audit"],('{0}' -f $createdGpo.content["audit"]),"ascii")) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting AUDIT policy content of GPO $($createdGpo.name) $($createdGpo.filePath["audit"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set AUDIT policy content of GPO $($createdGpo.name) $($createdGpo.filePath["audit"]) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set AUDIT policy content of GPO $($createdGpo.name) $($createdGpo.filePath["audit"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        write-output ("Setting GPT content of GPO $($createdGpo.name)")
        # set gpt first
        if ($myDomain.SetGpoContent($createdGpo.filePath["gpt"],('{0}' -f $createdGpo.content["gpt"]),"unicode")) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting policy content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set policy content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set policy content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("security","audit"))) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
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
function Set-MdiConfigFile {
    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [parameter(Mandatory=$false)]
        [string]$Path=$($PSScriptRoot),
        [parameter(Mandatory=$true)]
        [string]$AccessKey,
        [parameter(Mandatory=$true)]
        [string]$Identity,
        [Parameter(Mandatory=$false)]
        [string]$Proxy="`$null",
        [parameter(Mandatory=$false)]
        [string]$StorageAccountName,
        [parameter(Mandatory=$false)]
        [string]$SASToken,
        [parameter(Mandatory=$true)]
        [ValidateSet('Commercial', 'GCC', 'GCC-H', 'DOD')]
        [string]$CloudType,
        [Parameter(Mandatory=$false)]
        [switch]$IgnoreNPCAP,
        [parameter(Mandatory=$false)]
        [bool]$WriteEventLog=$false
    )
    $outputFile = "config.psd1"
    $contents = "@{" + [environment]::NewLine
    $contents += "`t# When updating `$null values, the value needs to be in single quotes ex. `$null => 'AccessKey'{0}" -f [environment]::NewLine
    $contents += ("`tAccessKey          = '{0}'  {1}{2}" -f $AccessKey,'# The access Key from the MDI portal https://portal.atp.azure.com/',[environment]::NewLine)
    $contents += ("`tIdentity           = '{0}'  {1}{2}" -f $Identity,'# The SAM Account name of the Service or Group Manged Service Account (remember the $ with gMSA)',[environment]::NewLine)
    if ($Proxy -eq "`$null") {
        $contents += ("`tProxy              = {0}  {1}{2}" -f '$null','# The full proxy url i.e. http://ip:port',[environment]::NewLine)
    } else {
        $contents += ("`tProxy              = '{0}'  {1}{2}" -f $Proxy,'# The full proxy url i.e. http://ip:port',[environment]::NewLine)
    }
    if (-not ([string]::IsNullOrEmpty($SASToken))) {
        $contents += "`t# DELETE THESE TWO LINES AFTER THE ENGAGEMENT IS COMPLETE SO THE LOG UPLOAD AUTOMATICALLY STOPS{0}" -f [environment]::NewLine
        $contents += ("`tStorageAccountName = '{0}'  {1}{2}" -f $StorageAccountName,"# Starts with 'ircstc'. Found in the welcome email.",[environment]::NewLine)
        $contents += ("`tSASToken           = '{0}'  {1}{2}" -f $SASToken,"# Starts with '?sv='.   Found in the welcome email.",[environment]::NewLine)
    }
    $contents += ("`tCloudType          = '{0}'  {1}{2}" -f $CloudType,'# Required (Commercial, GCC, GCC-H, DOD) This to target log storage.',[environment]::NewLine)
    #$contents += ("`tWriteEventLog      = `$$WriteEventLog{0}" -f [environment]::NewLine)
    $contents += ("`tIgnoreNPCAP        = `$$IgnoreNPCAP  {0}{1}" -f '# Skips the NPCAP checks. Use only when you need to run a different version or different configuration of NPCAP. USE WITH CAUTION.',[environment]::NewLine)
    $contents += "}"
    Set-Content -Path ('{0}\{1}' -f $Path,$outputFile) -value $contents -force
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

function Set-MDIDeletedObjectsContainerPermission {
    [CmdletBinding()]
    Param(
        [parameter(Mandatory = $True, Position = 1)]
        [string]$Identity,
        [parameter(Mandatory = $true)]
        [ValidateSet('Domain', 'Forest')]
        [string]$IdentityLocation,
        [parameter(Mandatory = $false)]
        [switch]$RemovePermissions,
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )
    $returnVal = $false
    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
    $dn = $myDomain.domainDn
    $LDAPFilter = '(&(|(samaccountname={0})(samaccountname={0}$))(|(objectClass=user)(objectClass=msDS-GroupManagedServiceAccount)))' -f $Identity
    $foundIdentity = ($myDomain.GetObjectByFilter($LDAPFilter, $IdentityLocation)).properties.samaccountname
    $Identity = $(if(-not [string]::IsNullOrEmpty($foundIdentity)) {$foundIdentity} else {$Identity})
    if ($IdentityLocation -eq 'Domain') {
        $server = $myDomain.chosenDc
        $id = ("{0}\{1}" -f $myDomain.netbiosname, $Identity)
    } else {
        $server = $myDomain.forestDetail.PDCEmulator
        $id = ("{0}\{1}" -f $myDomain.forestNetbiosName, $Identity)
    }
    try {
        if ($RemovePermissions) {
            $parameters = @{
                ArgumentList = $dn, $id
                ScriptBlock  = {
                    Param ($param1, $param2)
                    $deletedObjectsDN = "\\$server\CN=Deleted Objects,{0}" -f $param1
                    $params = @("$deletedObjectsDN", '/takeOwnership')
                    & "$($env:SystemRoot)\system32\dsacls.exe" $params
                    $params = @("$deletedObjectsDN", '/R', "$($param2)")
                    & "$($env:SystemRoot)\system32\dsacls.exe" $params
                }
            }
        } else {
            $parameters = @{
                ArgumentList = $dn, $id
                ScriptBlock  = {
                    Param ($param1, $param2)
                    $deletedObjectsDN = "\\$server\CN=Deleted Objects,{0}" -f $param1
                    $params = @("$deletedObjectsDN", '/takeOwnership')
                    & "$($env:SystemRoot)\system32\dsacls.exe" $params
                    $params = @("$deletedObjectsDN", '/G', "$($param2):LCRP")
                    & "$($env:SystemRoot)\system32\dsacls.exe" $params
                }
            }
        }
        $command = "Invoke-command @parameters"
        $dsaclCheck = Invoke-Expression $command
        $returnVal = $true
    } catch {
        Write-Error "Failed to set Deleted Objects permissions"
    }
    return $returnVal
}
function Set-MdiDeployGpo {
    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [parameter(Mandatory=$false)]
        [string]$sourcePath,
        [Parameter(Mandatory=$true)]
        $myDomain
    )
    if (!($sourcePath)) {
        $sourcePath = "\\$($myDomain.domainFqdn)\NETLOGON\mss"
    }
    $whenChanged = $(get-date -f "yyyy-MM-dd HH:mm:ss")
    $gpoFileHeader = New-GpoHeader
    $createdGpo.filePath.Add("env", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\Preferences\EnvironmentVariables\EnvironmentVariables.xml')
    $createdGpo.filePath.Add("files", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\Preferences\Files\Files.xml')
    $createdGpo.filePath.Add("tasks", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\Preferences\ScheduledTasks\ScheduledTasks.xml')
    $createdGpo.content.Add("env",('<?xml version="1.0" encoding="utf-8"?>{0}' -f [environment]::NewLine))
    $createdGpo.content["env"] += '<EnvironmentVariables clsid="{BF141A63-327B-438a-B9BF-2C188F13B7AD}">'
    $createdGpo.content["env"] += [environment]::NewLine
    $createdGpo.content["env"] += '<EnvironmentVariable clsid="{78570023-8373-4a19-BA80-2F150738EA19}" name="mySourcePath" status="mySourcePath = '+$sourcePath+'" image="1" changed="'+$whenChanged+'" uid="{'+$((new-guid).guid.toupper())+'}" desc="The source location to the MDI Package. Recommended on sysvol or a share accessable by all systems." removePolicy="1" bypassErrors="1">'
    $createdGpo.content["env"] += [environment]::NewLine
    $createdGpo.content["env"] += '<Properties action="R" name="mySourcePath" value="'+$sourcePath+'" user="0" partial="0"/>'
    $createdGpo.content["env"] += [environment]::NewLine
    $createdGpo.content["env"] += '</EnvironmentVariable>{0}' -f [environment]::NewLine
    $createdGpo.content["env"] += '<EnvironmentVariable clsid="{78570023-8373-4a19-BA80-2F150738EA19}" name="myDestPath" status="myDestPath = c:\windows\temp\mss" image="1" changed="'+$whenChanged+'" uid="{'+$((new-guid).guid.toupper())+'}" desc="The destination path on the client where all needed files will be copied" removePolicy="1" bypassErrors="1">'
    $createdGpo.content["env"] += [environment]::NewLine
    $createdGpo.content["env"] += '<Properties action="R" name="myDestPath" value="c:\windows\temp\mss" user="0" partial="0"/>{0}' -f [environment]::NewLine
    $createdGpo.content["env"] += '</EnvironmentVariable>{0}' -f [environment]::NewLine
    $createdGpo.content["env"] += '</EnvironmentVariables>'
    $createdGpo.content.Add("files",('<?xml version="1.0" encoding="utf-8"?>{0}' -f [environment]::NewLine))
    $createdGpo.content["files"] += '<Files clsid="{215B2E53-57CE-475c-80FE-9EEC14635851}">'
    $createdGpo.content["files"] += [environment]::NewLine
    $createdGpo.content["files"] += '<File clsid="{50BE44C8-567A-4ed1-B1D0-9234FE1F38AF}" name="config.psd1" status="config.psd1" image="1" changed="'+$whenChanged+'" uid="{'+$((new-guid).guid.toupper())+'}" desc="Copies config.psd1 to the client system" bypassErrors="1" removePolicy="1">'
    $createdGpo.content["files"] += [environment]::NewLine
    $createdGpo.content["files"] += '<Properties action="R" fromPath="%mySourcePath%\mdisetup\config.psd1" targetPath="%myDestPath%\mdisetup\config.psd1" readOnly="0" archive="1" hidden="0" suppress="0"/>'
    $createdGpo.content["files"] += [environment]::NewLine
    $createdGpo.content["files"] += '</File>{0}' -f [environment]::NewLine
    $createdGpo.content["files"] += '<File clsid="{50BE44C8-567A-4ed1-B1D0-9234FE1F38AF}" name="Invoke-MDISetup.ps1" status="Invoke-MDISetup.ps1" image="1" changed="'+$whenChanged+'" uid="{'+$((new-guid).guid.toupper())+'}" desc="Copies Invoke-MDISetup.ps1 to the client system" bypassErrors="1" removePolicy="1">'
    $createdGpo.content["files"] += [environment]::NewLine
    $createdGpo.content["files"] += '<Properties action="R" fromPath="%mySourcePath%\mdisetup\Invoke-MDISetup.ps1" targetPath="%myDestPath%\mdisetup\Invoke-MDISetup.ps1" readOnly="0" archive="1" hidden="0" suppress="0"/>'
    $createdGpo.content["files"] += [environment]::NewLine
    $createdGpo.content["files"] += '</File>{0}' -f [environment]::NewLine
    $createdGpo.content["files"] += '</Files>{0}' -f [environment]::NewLine
    $createdGpo.content.Add("tasks",('<?xml version="1.0" encoding="utf-8"?>{0}' -f [environment]::NewLine))
    $createdGpo.content["tasks"] += '<ScheduledTasks clsid="{CC63F200-7309-4ba0-B154-A71CD118DBCC}">'
    $createdGpo.content["tasks"] += [environment]::NewLine
    $createdGpo.content["tasks"] += '<TaskV2 clsid="{D8896631-B747-47a7-84A6-C155337F3BC8}" name="MDI Setup" image="2" changed="'+$whenChanged+'" uid="{'+$((new-guid).guid.toupper())+'}" userContext="0" removePolicy="1">'
    $createdGpo.content["tasks"] += [environment]::NewLine
    $createdGpo.content["tasks"] += '<Properties name="MDI Setup" logonType="S4U" runAs="NT AUTHORITY\System" action="R">{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Task version="1.2">{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<RegistrationInfo>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Author>Microsoft Services</Author>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Description></Description>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '</RegistrationInfo>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Principals>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Principal id="Author">{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<UserId>NT AUTHORITY\System</UserId>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<LogonType>S4U</LogonType>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<RunLevel>HighestAvailable</RunLevel>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '</Principal>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '</Principals>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Settings>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<IdleSettings>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Duration>PT10M</Duration>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<WaitTimeout>PT1H</WaitTimeout>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<StopOnIdleEnd>false</StopOnIdleEnd>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<RestartOnIdle>false</RestartOnIdle>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '</IdleSettings>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<AllowHardTerminate>false</AllowHardTerminate>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<AllowStartOnDemand>true</AllowStartOnDemand>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Enabled>true</Enabled>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Hidden>false</Hidden>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<ExecutionTimeLimit>PT1H</ExecutionTimeLimit>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Priority>7</Priority>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '</Settings>{0}' -f [environment]::NewLine
    # triggers
    $createdGpo.content["tasks"] += '<Triggers>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<RegistrationTrigger>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Enabled>true</Enabled>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '</RegistrationTrigger>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '</Triggers>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Actions Context="Author"><Exec><Command>powershell.exe</Command><Arguments>-command "{dir $env:myDestPath\mdisetup | Unblock-File}.Invoke()"</Arguments></Exec>'
    $createdGpo.content["tasks"] += '{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Exec><Command>powershell.exe</Command><Arguments>-executionpolicy RemoteSigned -f %myDestPath%\mdisetup\Invoke-MDISetup.ps1</Arguments></Exec>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '</Actions></Task>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '</Properties>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Filters>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<FilterCollection bool="AND" not="0"><FilterFile bool="AND" not="0" path="%myDestPath%\mdisetup\Invoke-MDISetup.ps1" type="EXISTS" folder="0"/><FilterFile bool="AND" not="0" path="%myDestPath%\mdisetup\config.psd1" type="EXISTS" folder="0"/><FilterRegistry bool="AND" not="1" type="KEYEXISTS" hive="HKEY_LOCAL_MACHINE" key="SYSTEM\CurrentControlSet\Services\AATPSensor" valueName="" valueType="" valueData="" min="0.0.0.0" max="0.0.0.0" gte="1" lte="0"/></FilterCollection></Filters>'
    $createdGpo.content["tasks"] += [environment]::NewLine
    $createdGpo.content["tasks"] += '</TaskV2>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '</ScheduledTasks>{0}' -f [environment]::NewLine
    try {
        write-output ("Setting environment content of GPO $($createdGpo.name)")
        # this is multipart, set audit first
        if ($myDomain.SetGpoContent($createdGpo.filePath["env"],('{0}' -f $createdGpo.content["env"]),"UTF8")) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting environment policy content of GPO $($createdGpo.name) $($createdGpo.filePath["env"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set environment policy content of GPO $($createdGpo.name) $($createdGpo.filePath["env"]) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set environment policy content of GPO $($createdGpo.name) $($createdGpo.filePath["env"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        write-output ("Setting files content of GPO $($createdGpo.name)")
        # set gpt second
        if ($myDomain.SetGpoContent($createdGpo.filePath["files"],('{0}' -f $createdGpo.content["files"]),"UTF8")) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting files policy content of GPO $($createdGpo.name) $($createdGpo.filePath["files"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set files content of GPO $($createdGpo.name) $($createdGpo.filePath["files"]) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set files policy content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        write-output ("Setting tasks content of GPO $($createdGpo.name)")
        # set gpt second
        if ($myDomain.SetGpoContent($createdGpo.filePath["tasks"],('{0}' -f $createdGpo.content["tasks"]),"UTF8")) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting tasks policy content of GPO $($createdGpo.name) $($createdGpo.filePath["files"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set tasks content of GPO $($createdGpo.name) $($createdGpo.filePath["tasks"]) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set tasks policy content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        #[{00000000-0000-0000-0000-000000000000}{35141B6B-498A-4CC7-AD59-CEF93D89B2CE}{3BAE7E51-E3F4-41D0-853D-9BB9FD47605F}{CAB54552-DEEA-4691-817E-ED4A4D1AFC72}{35141B6B-498A-4CC7-AD59-CEF93D89B2CE}{35141B6B-498A-4CC7-AD59-CEF93D89B2CE}][{0E28E245-9368-4853-AD84-6DA3BA35BB75}{35141B6B-498A-4CC7-AD59-CEF93D89B2CE}][{7150F9BF-48AD-4DA4-A49C-29EF4A8369BA}{3BAE7E51-E3F4-41D0-853D-9BB9FD47605F}][{AADCED64-746C-4633-A97C-D61349046527}{CAB54552-DEEA-4691-817E-ED4A4D1AFC72}][{0E28E245-9368-4853-AD84-6DA3BA35BB75}{35141B6B-498A-4CC7-AD59-CEF93D89B2CE}][{0E28E245-9368-4853-AD84-6DA3BA35BB75}{35141B6B-498A-4CC7-AD59-CEF93D89B2CE}]
        if ($myDomain.SetGpoGpcExtensionRaw($createdGpo.guid,"[{00000000-0000-0000-0000-000000000000}{35141B6B-498A-4CC7-AD59-CEF93D89B2CE}{3BAE7E51-E3F4-41D0-853D-9BB9FD47605F}{CAB54552-DEEA-4691-817E-ED4A4D1AFC72}{35141B6B-498A-4CC7-AD59-CEF93D89B2CE}{35141B6B-498A-4CC7-AD59-CEF93D89B2CE}][{0E28E245-9368-4853-AD84-6DA3BA35BB75}{35141B6B-498A-4CC7-AD59-CEF93D89B2CE}][{7150F9BF-48AD-4DA4-A49C-29EF4A8369BA}{3BAE7E51-E3F4-41D0-853D-9BB9FD47605F}][{AADCED64-746C-4633-A97C-D61349046527}{CAB54552-DEEA-4691-817E-ED4A4D1AFC72}][{0E28E245-9368-4853-AD84-6DA3BA35BB75}{35141B6B-498A-4CC7-AD59-CEF93D89B2CE}][{0E28E245-9368-4853-AD84-6DA3BA35BB75}{35141B6B-498A-4CC7-AD59-CEF93D89B2CE}]")) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
}
function Set-MDIDomainAuditPermissions {
    ##########################################################################################################
    <#
    .SYNOPSIS
        Sets required auditing at the AD level for MDI to pick up event 4662
    
    .DESCRIPTION
        Audits for Everyone performing the actions CreateChild, DeleteChild, Self, WriteProperty, DeleteTree, ExtendedRight, Delete, WriteDacl, WriteOwner on users, groups, computers, and managed service accounts.

    .EXAMPLE
        Set-MDIDomainAuditPermissions

    .OUTPUTS
        None.

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
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )
    $returnVal = $false
    #Get domain object
    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain 
    if (!($myDomain)) {
        write-error "Failed to discover AD"
        throw
    } 
    try {
        
        #Descendant users bf967aba-0de6-11d0-a285-00aa003049e2
        #Descendant comps bf967a86-0de6-11d0-a285-00aa003049e2
        #Descendant groups bf967a9c-0de6-11d0-a285-00aa003049e2
        #Msds-managedserviceaccounts ce206244-5827-4a86-ba1c-1c0c386c1b64
        #Msds-groupmanagedserviceaccounts 7b8b558a-93a5-4af7-adca-c017e67f1057
        $parameters = @{
            ComputerName = $($myDomain.chosenDc)
            ScriptBlock  = {
                Param ($param1)
                ipmo ActiveDirectory -force
                set-location AD:
                $user = [Security.principal.NTAccount]"Everyone"
                $guids=@("bf967aba-0de6-11d0-a285-00aa003049e2", "bf967a86-0de6-11d0-a285-00aa003049e2", "bf967a9c-0de6-11d0-a285-00aa003049e2", "ce206244-5827-4a86-ba1c-1c0c386c1b64", "7b8b558a-93a5-4af7-adca-c017e67f1057")
                $rights=@("CreateChild, DeleteChild, Self, WriteProperty, DeleteTree, ExtendedRight, Delete, WriteDacl, WriteOwner")
                $auditFlags="Success"
                $inheritanceFlags="Descendents"
                $acl = get-acl -path "AD:\$param1" -Audit
                if ($acl) {
                    foreach ($guid in $guids) {
                        $modified=$false
                        $auditRuleObject = New-Object System.DirectoryServices.ActiveDirectoryAuditRule($user,$rights,$auditFlags, $inheritanceFlags,[guid]$guid)
                        $acl.AddAuditRule($AuditRuleObject)
                    }
                    set-acl -path "AD:\$param1" $acl
                    
                }

            }
            ArgumentList = $($myDomain.domainDn)
        }
        $command  = "Invoke-command @parameters"
        iex $command
        $returnVal = $true
    } catch {
        $errorMessage = $_.Exception
        write-error "$server`: Failed to set MDI Domain Audit permissions"
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$server`: Failed to set MDI Domain Audit permissions. $errorMessage" -logSev "Error" | out-null
        $returnVal = $false
    } finally {
        set-location $env:systemroot
    }
    return $returnVal
}
function Set-MDIDomainReadiness {
        ##########################################################################################################
    <#
    .SYNOPSIS
        Fixes domain for MDI readiness.

    .DESCRIPTION
        Fixes domain for MDI readiness.

    .EXAMPLE
        Set-MDIDomainReadiness

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
    [CmdletBinding(DefaultParameterSetName='manual')]
    Param(
        [Parameter(Mandatory = $false,ValueFromPipeline = $true,ParameterSetName='pipeline',DontShow)]
        [MdiDomainReadiness]$pipelineInput,
        [Parameter(Mandatory = $true,ParameterSetName='manual')]
        [ValidateLength(1,15)]
        [string]$Identity,
        [parameter(Mandatory = $true,ParameterSetName='manual')]
        [ValidateSet('Domain', 'Forest')]
        [string]$IdentityLocation,
        [Parameter(Mandatory = $false,ParameterSetName='manual')]
        [Parameter(ParameterSetName='pipeline')]
        [switch]$ForceStandardAccount,
        [Parameter(Mandatory=$false,ParameterSetName='manual')] # make sure to add a reference for any new GPO here
        [Parameter(ParameterSetName='createGpo')]
        [ValidateSet('AdvancedAuditPolicyCAs', 'AdvancedAuditPolicyDCs', 'EntraIDAuditing',
            'LogonAsService', 'NTLMAuditing', 'PerformanceLib', 'ProcessorPerformance', 'All')] [string[]]$GposToCreate = @("All"),
        [Parameter(Mandatory = $false,ParameterSetName='manual')]
        [Parameter(ParameterSetName='pipeline')]
        [switch]$NoCreateGpos,
        [Parameter(Mandatory = $false,ParameterSetName='manual')]
        [Parameter(ParameterSetName='pipeline')]
        [switch]$NoSetRequiredAudit,
        [parameter(Mandatory=$false)]
        [switch]$Force,
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )
    DynamicParam {
        
    }
    begin {
        
    }

    process {
        # if pipline input then myMdiDomainReadiness = pipeline input
        if ($pipelineInput) {
            $myMdiDomainReadiness = $pipelineInput
        } else {
            # else manually build the class
            $mdiDomainReadinessParams = @{
                Identity            = $identity
                IdentityLocation    = $identityLocation
            }
            if (-not ([string]::IsNullOrEmpty($domain))) {
                $mdiDomainReadinessParams.Add("domain",$domain)
            }
            [MdiDomainReadiness]$myMdiDomainReadiness = Get-MDIDomainReadiness @mdiDomainReadinessParams
        }
        $myDomain = $myMdiDomainReadiness.myDomain
        if (($myDomain.forestSid -ne $myDomain.domainSid) -and ($IdentityLocation -eq 'Forest') -and (-not (Test-MDIUserInEnterpriseAdmins))) {
            write-warning "Must be ENTERPRISE ADMIN to work with KDS in a child domain!"
            throw
        }
        if ($Force) {
            if ($myMdiDomainReadiness.identityExists) {
                $possibleGroupName = "{0}-GMSAPwdRet" -f $(($myMdiDomainReadiness.identity).replace('$','').substring(0, ([System.Math]::Min(16, ($myMdiDomainReadiness.identity).Length)-1)))
                $identityObj = $myDomain.GetObjectByName($myMdiDomainReadiness.identity,$myMdiDomainReadiness.identityLocation,$false)
                $groupObj = $myDomain.GetObjectByName($possibleGroupName,$myMdiDomainReadiness.identityLocation,$false)
                try {
                    if ($identityObj) {
                        $identityDn = $identityObj.Properties["distinguishedname"]
                        if ($myDomain.RemoveObjectByDn($identityDn,$myMdiDomainReadiness.IdentityLocation)) {
                            $myMdiDomainReadiness.identityExists = $false
                            if ($groupObj) {
                                $groupDn = $groupObj.Properties["distinguishedname"]
                                if (!($myDomain.RemoveObjectByDn($groupDn,$myMdiDomainReadiness.identityLocation))) {
                                    Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to remove group $possibleGroupName from $($myMdiDomainReadiness.IdentityLocation). $($_.Exception)" -logSev "Warn" | out-null
                                    throw
                                }
                            }
                        } else {
                            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to remove identity $($myMdiDomainReadiness.identity) from $($myMdiDomainReadiness.IdentityLocation). $($_.Exception)" -logSev "Warn" | out-null
                            throw
                        }
                    } else {
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to find identity $($myMdiDomainReadiness.identity) from $($myMdiDomainReadiness.IdentityLocation)." -logSev "Warn" | out-null
                        throw
                    }
                } catch {
                    Write-Warning "Failed to remove identity and group $($myMdiDomainReadiness.identity) and $possibleGroupName from $($myMdiDomainReadiness.IdentityLocation)"
                    Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to remove identity and group $($myMdiDomainReadiness.identity) and $possibleGroupName from $($myMdiDomainReadiness.IdentityLocation). $($_.Exception)" -logSev "Warn" | out-null
                }
            }
        }
        if (!($myMdiDomainReadiness.identityExists)) {
            try {
                if ($myMdiDomainReadiness.identity) {
                    write-host "Creating account"
                    $newMdiDsaParams = @{
                        myDomain = $myDomain
                        IdentityLocation = $myMdiDomainReadiness.identityLocation
                        Identity = $myMdiDomainReadiness.identity
                    }
                    if (-not $myMdiDomainReadiness.isGmsa) {
                        $newMdiDsaParams.Add("forceStandardAccount", $true)
                    } else {
                        $newMdiDsaParams.Add("GmsaGroupName", "{0}-GMSAPwdRet" -f $(($myMdiDomainReadiness.identity).replace('$','')))
                    }
                    if (New-MDIDSA @newMdiDsaParams) {
                        start-sleep 1
                        $adObject = $mydomain.GetObjectByFilter("|(samaccountname=$($myMdiDomainReadiness.identity))(samaccountname=$($myMdiDomainReadiness.identity)$)",$myMdiDomainReadiness.identityLocation)
                        $myMdiDomainReadiness.identitySid = $($myDomain.GetStringSidFromBytes([byte[]]$($adObject.properties.objectsid)))
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Created account: $($myMdiDomainReadiness.identity) with SID: $($myMdiDomainReadiness.identitySid)" -logSev "Info" | out-null
                        $myMdiDomainReadiness.identityExists = $true
                    } else {
                        throw
                    }
                } else {
                    write-error "Readiness object has no reference to service account name. How did you get here?"
                    Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Readiness object has no reference to service account name. How did you get here?" -logSev "Error" | out-null
                    throw
                }
            } catch {
                $errorMessage = $_.Exception
                write-error "Failed to create service account"
                Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to create service account. $errorMessage" -logSev "Error" | out-null
                # we end here
                throw
            }
        } else {
            Write-Output "Account $($myMdiDomainReadiness.identity) already exists"
        }
        if (!($noCreateGPOs)) {
            # we're creating GPO's
            if ($($myMdiDomainReadiness.identityExists)) {
                New-MdiGpo -gposToCreate $GposToCreate -identity $myMdiDomainReadiness.identity -myDomain $myMdiDomainReadiness.myDomain -Force:$Force
            } else {
                write-warning "$($myMdiDomainReadiness.identity) does not appear to exist. Can't create GPO's: $($myMdiDomainReadiness.LogonAsService.GpoName), $($myMdiDomainReadiness.EntraIDAuditing.GpoName)"
                Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($myMdiDomainReadiness.identity) does not appear to exist. Can't create GPO: $($myMdiDomainReadiness.LogonAsService.GpoName), $($myMdiDomainReadiness.EntraIDAuditing.GpoName)" -logSev "Warn" | out-null
                New-MdiGpo -gposToCreate AdvancedAuditPolicyCAs, AdvancedAuditPolicyDCs, NTLMAuditing, PerformanceLib, ProcessorPerformance -identity $myMdiDomainReadiness.identity -myDomain $myMdiDomainReadiness.myDomain -Force:$Force
            }
        } else {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "No create GPO's was specified" -logSev "Info" | out-null
        }
        if (!($noSetRequiredAudit)) {
            # we're setting all the audits
            if ($myMdiDomainReadiness.pkiAuditPoliciesNeeded) {
                try {
                    $certServers = Get-ADObject -LDAPFilter "(objectClass=certificationAuthorities)" -SearchBase "CN=Certification Authorities,CN=Public Key Services,CN=Services,CN=Configuration,$($mydomain.forestdetail.DistinguishedName)"
                } catch {
                    write-warning "No Certificate Authorities found at CN=Certification Authorities,CN=Public Key Services,CN=Services,CN=Configuration,$($mydomain.forestdetail.DistinguishedName)"
                    Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "No Certificate Authorities found at CN=Certificate Authorities,CN=Public Key Services,CN=Services,CN=Configuration,$($mydomain.forestdetail.DistinguishedName). But PKI needed check passed, this indicates ADCS was manually cleaned." -logSev "Warn" | out-null
                }
                if ($certServers) {
                    foreach ($certServer in $certServers) {
                        try {
                            if (Set-MDIPkiAuditing -server $certServer.name) {
                                write-output "Set PKI auditing on server $($certServer.name)"
                            } else {
                                throw
                            }
                        } catch {
                            $errorMessage = $_.Exception
                            write-error "Failed to set PKI auditing on server $($certServer.name)"
                            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set PKI auditing on server $($certServer.name). $errorMessage" -logSev "Error" | out-null
                        }
                    }
                }
            }
            if ($myMdiDomainReadiness.adfsAuditPoliciesNeeded) {
                try {
                    if (Set-MDIAdfsAuditPermissions -myDomain $myDomain) {
                        write-output "Set ADFS object permissions"
                    } else {
                            throw
                    }
                } catch {
                    $errorMessage = $_.Exception
                    write-error "Failed to set ADFS object permissions"
                    Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set ADFS object permissions. $errorMessage" -logSev "Error" | out-null
                }
            }
            if ($myMdiDomainReadiness.exchangeAuditPoliciesNeeded) {
                try {
                    if (Set-MDIExchangeAuditPermissions -myDomain $myDomain) {
                        write-output "Set Exchange object permissions"
                    } else {
                            throw
                    }
                } catch {
                    $errorMessage = $_.Exception
                    write-error "Failed to set Exchange object permissions"
                    Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set Exchange object permissions. $errorMessage" -logSev "Error" | out-null
                }
            }
            if ($true) { #eventually this may become if domain audit policies needed, but that's a problem for future me. or future you if you're someone other than me reading this
                try {
                    if (Set-MDIDeletedObjectsContainerPermission -Identity $myMdiDomainReadiness.identity -IdentityLocation $myMdiDomainReadiness.identityLocation -myDomain $myDomain) {
                        write-output "Set AD deleted object permissions"
                    } else {
                        throw
                    }
                } catch {
                    $errorMessage = $_.Exception
                    write-error "Failed to set AD deleted object permissions"
                    Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set AD deleted object permissions. $errorMessage" -logSev "Error" | out-null
                }
                try {
                    if (Set-MDIDomainAuditPermissions -myDomain $myDomain) {
                        write-output "Set AD root object permissions"
                    } else {
                        throw
                    }
                } catch {
                    $errorMessage = $_.Exception
                    write-error "Failed to set AD root object permissions"
                    Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set AD root object permissions. $errorMessage" -logSev "Error" | out-null
                }
            }
        } else {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "No set required audit was specified" -logSev "Info" | out-null
        }
    }
}
function Set-MdiEntraAuditGpo {
    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [Parameter(Mandatory=$true)]
        $myDomain
    )
    #we don't need the gpo header unless it's a standard GPO
    $gpoFileHeader = New-GpoHeader
    $createdGpo.filePath.Add("audit", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\microsoft\windows nt\Audit\audit.csv')

    # build block for MDI-All Audit Policies (Audit settings only)
    $createdGpo.content.Add("audit", 'Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value{0}' -f [environment]::NewLine)
    $createdGpo.content["audit"] += ',System,Audit Logon,{0cce9215-69ae-11d9-bed3-505054503030},Success and Failure,,3'

    try {
        write-output ("Setting audit content of GPO $($createdGpo.name)")
        # this is multipart, set audit first
        if ($myDomain.SetGpoContent($createdGpo.filePath["audit"],('{0}' -f $createdGpo.content["audit"]),"ascii")) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting AUDIT policy content of GPO $($createdGpo.name) $($createdGpo.filePath["audit"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set AUDIT policy content of GPO $($createdGpo.name) $($createdGpo.filePath["audit"]) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set AUDIT policy content of GPO $($createdGpo.name) $($createdGpo.filePath["audit"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("audit"))) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
}
function Set-MDIExchangeAuditPermissions {
    ##########################################################################################################
    <#
    .SYNOPSIS
        Sets required auditing at the AD level for MDI to pick up Exchange events
    
    .DESCRIPTION
        Audits for Everyone performing the actions Write All Properties for All objects at the AD configuration context.

    .EXAMPLE
        Set-MDIExchangeAuditPermissions


    .OUTPUTS
        None.

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
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )
    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain 
    $returnVal = $false
    
    if (([System.DirectoryServices.DirectoryEntry]::Exists('LDAP://CN=Microsoft Exchange,CN=Services,CN=Configuration,{0}' -f $myDomain.forestDetail.DistinguishedName))) {
        try {
            #configuration context is forest level
            $sb = {
                param($pConfigContext)
                import-module activedirectory -force
                sl AD:\
                $acl = ($acl = get-acl -path "AD:\$pConfigContext" -Audit)
                $user = [Security.principal.NTAccount]"Everyone"
                $guids=@("00000000-0000-0000-0000-000000000000")
                $rights=@('WriteProperty')
                $auditFlags=@("Success","Failure")
                $inheritanceType="All"
                foreach ($guid in $guids) {
                    foreach ($auditFlag in $auditFlags) {
                        $modified=$false
                        $auditRuleObject = New-Object System.DirectoryServices.ActiveDirectoryAuditRule($user,$rights,$auditFlag, $inheritanceType,[guid]$guid)
                        $acl.AddAuditRule($AuditRuleObject)
                    }
                }
                set-acl "AD:\$pConfigContext" $acl
            }
            Invoke-Command -scriptBlock $sb -ArgumentList ('CN=Configuration,{0}' -f $myDomain.forestDetail.DistinguishedName)
            $returnVal = $true
        }
        catch {
            $errorMessage = $_.Exception
            write-error "$($myDomain.domainFqdn)`: Failed to set MDI Exchange Audit permissions"
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($myDomain.domainFqdn)`: Failed to set MDI Exchange Audit permissions. $errorMessage" -logSev "Error" | out-null
        } finally {
            set-location $env:systemroot
        }
    }
    return $returnVal
}
function Set-MdiGpoDeployScript {
    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$false)]
        [string]$Path="$env:temp\mss\MDISetup"
    )

    try {
        $Path = "$env:temp\MSS\MDISetup"
        $outputFile = "Invoke-MdiSetup.ps1"
        $modulePath = $((get-module mircat).path)
        if (-not (Test-Path "$Path")) {
            $null = new-item -type Directory -path "$Path"
        }
        try {
            $null = remove-item -Path $("{0}\{1}" -f $Path,$outputFile) -ErrorAction SilentlyContinue
        } catch {}
        $moduleContent = get-content "$modulePath"
        if (-not [string]::IsNullOrEmpty($moduleContent)) {
            $streamWriter = [System.IO.StreamWriter]::new(("{0}\{1}" -f $Path,$outputFile), [System.IO.FileMode]::Create)
            $streamWriter.WriteLine("begin {")
            $write = $true
            $moduleContent | % { 
                if (($_ -match '(function\s\w+\-\w+)|(function\s\w+\-\w+\s\{)')) { 
                    $write = $false
                }
                if ($write) {
                    $streamWriter.WriteLine($_ )
                }
            }
            $functionsToGet = @('Get-MDIServerReadiness','Set-MDIServerReadiness','Get-MDICipherSuite','Set-MDICipherSuite','Get-MDICpuScheduler','Set-MDICpuScheduler','Get-MDINetFrameworkVersion','Get-MDINpcap','Set-MDINpcap','Get-MDIPerfCounter','Set-MDIPerfCounter','Get-MDIRootCert','Set-MDIRootCert','Test-MDISensorApiConnection','Invoke-MdiOnboard','Test-MDIIsInstalled','Add-LogEntry','Set-ScriptParameters','Write-Log')
            foreach ($function in $functionsToGet) {
                $streamWriter.WriteLine([environment]::newline)
                $streamWriter.WriteLine("function $function {")
                foreach($line in $((get-command $function).Definition -split [environment]::newline)) {
                    $streamWriter.WriteLine($line)
                }
                $streamWriter.WriteLine("}")
            }
            #this is end of begin
            $streamWriter.WriteLine("}")
            $streamWriter.WriteLine("process {")
            # call invoke-mdionboard, it's insane to build logic here
            $streamWriter.WriteLine('$parameterMessage = Set-ScriptParameters -Path "$PSScriptRoot\config.psd1"')
            $streamWriter.WriteLine('Invoke-MdiOnboard -AccessKey $AccessKey')
            #this is end of process
            $streamWriter.WriteLine("}")
        } else {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage ("Unable to read module at path {0}" -f $modulePath) -logSev "Error" | out-null
            throw
        }
    } catch {
        write-error "Unable to generate file $Path\$outputFile"
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage ("Unable to generate file {0}\{1}. {2}" -f $Path,$outputFile,$($_.Exception)) -logSev "Error" | out-null
    } finally {
        if ([bool]($streamWriter)) {
            $streamWriter.Close()
        }
    }
}
function Set-MdiLogonServiceGpo {
    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [parameter(Mandatory=$true)]
        [string]$identity,
        [Parameter(Mandatory=$true)]
        $myDomain
    )
    
    # if the service account name has a $ it's gmsa
    # discover Tier 0 SID
    $ntAllServices="*S-1-5-80-0"
    $tier0ServiceAcctsObj = $myDomain.GetObjectByName("Tier 0 Service Accounts","DOMAIN",$false)
    if ($tier0ServiceAcctsObj -ne $null) {
        $tier0ServiceAccts = '*{0}' -f $($myDomain.GetStringSidFromBytes([byte[]]$($tier0ServiceAcctsObj.properties.objectsid)))
    }

    # get the SID of the MDI service account
    $adObject = $mydomain.GetObjectByFilter("|(samaccountname=$identity)(samaccountname=$($identity)$)","domain")
    if ([string]::IsNullOrEmpty($adObject)) {
        $adObject = $mydomain.GetObjectByFilter("|(samaccountname=$identity)(samaccountname=$($identity)$)","forest")
    }
    if (-not [string]::IsNullOrEmpty($adObject)) {
        $stringSid = '*{0}' -f $($myDomain.GetStringSidFromBytes([byte[]]$($adObject.properties.objectsid)))
    }

    $createdGpo.filePath.Add("gpt", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\microsoft\windows nt\SecEdit\GptTmpl.inf')

    # build block for MDI-All GPT Policies (GPT settings only)
    # we don't need the gpo header unless it's a standard GPO
    $gpoFileHeader = New-GpoHeader
    $createdGpo.content.Add("gpt", $gpoFileHeader)
    $createdGpo.content["gpt"] += [environment]::NewLine
    $createdGpo.content["gpt"] += '[Privilege Rights]{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'SeServiceLogonRight = {0}{1}' -f ($tier0ServiceAccts,$ntAllServices,$stringSid -ne $null -join ','),[environment]::NewLine

    try {
        write-output ("Setting content of GPO $($createdGpo.name)")
        # set gpt first
        if ($myDomain.SetGpoContent($createdGpo.filePath["gpt"],('{0}' -f $createdGpo.content["gpt"]),"unicode")) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting policy content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set policy content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set policy content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("security"))) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
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
function Set-MdiNtlmAuditDcGpo {
    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [Parameter(Mandatory=$true)]
        $myDomain
    )
    
    $gpoFileHeader = New-GpoHeader

    $createdGpo.filePath.Add("gpt", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\microsoft\windows nt\SecEdit\GptTmpl.inf')

    # build block for MDI-All GPT Policies (GPT settings only)
    $createdGpo.content.Add("gpt", $gpoFileHeader)
    $createdGpo.content["gpt"] += [environment]::NewLine
    $createdGpo.content["gpt"] += '[Registry Values]{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Control\Lsa\MSV1_0\AuditReceivingNTLMTraffic=4,2{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Control\Lsa\MSV1_0\RestrictSendingNTLMTraffic=4,1{0}'  -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Services\Netlogon\Parameters\AuditNTLMInDomain=4,7{0}'  -f [environment]::NewLine

    try {
        write-output ("Setting content of GPO $($createdGpo.name)")
        # set gpt first
        if ($myDomain.SetGpoContent($createdGpo.filePath["gpt"],('{0}' -f $createdGpo.content["gpt"]),"unicode")) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting policy content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set policy content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set policy content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("security"))) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
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
function Set-MdiPerfLibGpo {
    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [Parameter(Mandatory=$true)]
        $myDomain
    )
    #we don't need the gpo header unless it's a standard GPO
    $gpoFileHeader = New-GpoHeader
    $createdGpo.filePath.Add("gpt", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\microsoft\windows nt\SecEdit\GptTmpl.inf')

    $createdGpo.content.Add("gpt", $gpoFileHeader)
    $createdGpo.content["gpt"] += [environment]::NewLine
    $createdGpo.content["gpt"] += '[Registry Keys]{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += '"MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Perflib",0,"D:PAR(A;CI;KR;;;S-1-15-2-1)(A;CIIO;KA;;;CO)(A;CI;KR;;;S-1-5-19)(A;CI;KA;;;SY)(A;CI;KA;;;BA)(A;CI;KR;;;BU)"'

    try {
        write-output ("Setting content of GPO $($createdGpo.name)")
        # set gpt first
        if ($myDomain.SetGpoContent($createdGpo.filePath["gpt"],('{0}' -f $createdGpo.content["gpt"]),"unicode")) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting policy content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set policy content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set policy content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("security"))) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
}
function Set-MdiPerfPlanGpo {
    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [Parameter(Mandatory=$true)]
        $myDomain
    )
    if ($myDomain.credential -eq [System.Management.Automation.PSCredential]::Empty) {
        $gppSet = $false
        $gppParams = @{
            Guid      = $createdGpo.guid
            Type      = 'String'
            Key       = 'HKEY_LOCAL_MACHINE\Software\Policies\Microsoft\Power\PowerSettings'
            ValueName = 'ActivePowerScheme'
            Value     = '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'
        }; if (-not [string]::IsNullOrEmpty($myDomain.chosenDc)) { $gppParams.Add("Server", $myDomain.chosenDc) }
        try {
            $convertHash = ($gppParams | convertto-json | convertfrom-json)
            $loggedParams = $convertHash -join ','
        } catch {}
        try {
            write-output ("Setting content of GPO $($createdGpo.name)")
            # set registry first
            $gppSet = [bool](Set-GPRegistryValue @gppParams -ErrorAction SilentlyContinue)
            if ($gppSet) {
                Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting policy content of GPO $($createdGpo.name) in domain $($myDomain.domainFqdn) using Set-GPRegistryValue $loggedParams." -logSev "Info" | out-null
            } else {
                throw
            }
        } catch {
            $gppSet = $false
            $errorMessage = $_.Exception
            write-error "Failed to set policy content of GPO $($createdGpo.name) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set policy content of GPO $($createdGpo.name) in domain $($myDomain.domainFqdn) using Set-GPRegistryValue $loggedParams. $errorMessage" -logSev "Error" | out-null
        }
    }
    if ($gppSet -eq $false) {
        $createdGpo.filePath.Add("registry", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\Preferences\Registry\Registry.xml')

        # build block for MDI-All Registry Policies (Registry settings only)
        $createdGpo.content.Add("registry", '<?xml version="1.0" encoding="utf-8"?>{0}' -f [environment]::NewLine)
        $createdGpo.content["registry"] += '<RegistrySettings clsid="{A3CCFC41-DFDB-43a5-8D26-0FE8B954DA51}"><Registry clsid="{9CD4B2F4-923D-47f5-A062-E897DD1DAD50}" name="ActivePowerScheme" status="ActivePowerScheme" image="7" changed="2023-03-22 23:20:19" uid="{16193CB0-F01A-4B53-8198-8FA65C2EFFCF}"><Properties action="U" displayDecimal="0" default="0" hive="HKEY_LOCAL_MACHINE" key="Software\Policies\Microsoft\Power\PowerSettings" name="ActivePowerScheme" type="REG_SZ" value="8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"/></Registry>'
        $createdGpo.content["registry"] += '</RegistrySettings>{0}' -f [environment]::NewLine

        try {
            write-output ("Setting content of GPO $($createdGpo.name)")
            # set registry first
            if ($myDomain.SetGpoContent($createdGpo.filePath["registry"],('{0}' -f $createdGpo.content["registry"]),"UTF8")) {
                Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting policy content of GPO $($createdGpo.name) $($createdGpo.filePath["registry"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
            } else {
                throw
            }
        } catch {
            $errorMessage = $_.Exception
            write-error "Failed to set policy content of GPO $($createdGpo.name) $($createdGpo.filePath["registry"]) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set policy content of GPO $($createdGpo.name) $($createdGpo.filePath["registry"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
        }
        try {
            if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("registry"))) {
                Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
            } else {
                throw
            }
        } catch {
            $errorMessage = $_.Exception
            write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
        }
        try {
            if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
                Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
            } else {
                throw
            }
        } catch {
            $errorMessage = $_.Exception
            write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
        }
    }
}
function Set-MDIPkiAuditing {
    ##########################################################################################################
    <#
    .SYNOPSIS
        Sets PKI ADCS auditiing.

    .DESCRIPTION
        Sets PKI ADCS auditing.

    .EXAMPLE
        Set-MDIPkiAuditing -server myadcs.contoso.com

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
    try {
        @('certutil -setreg CA\AuditFilter 127','restart-service certsvc') | % {
            if ($server -ne "localhost") {
                $command  = "Invoke-command -scriptblock {$_} -ComputerName $server"+' 2> $null'
            } else {
                $command  = "Invoke-command -scriptblock {$_}"+' 2> $null'
            }
            $certCommand = iex $command
        }
        $returnVal = $true
    } catch {
        $errorMessage = $_.Exception
        write-error "$server`: Failed to set PKI auditing"
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$server`: Failed to set PKI auditing. $errorMessage" -logSev "Error" | out-null
    }
    return $returnVal
}
function Set-MdiRemoveGpo {
    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [Parameter(Mandatory=$true)]
        $myDomain
    )
    $whenChanged = $(get-date -f "yyyy-MM-dd HH:mm:ss")
    $createdGpo.filePath.Add("tasks", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\Preferences\ScheduledTasks\ScheduledTasks.xml')
    $createdGpo.content.Add("tasks",('<?xml version="1.0" encoding="utf-8"?>{0}' -f [environment]::NewLine))
    $createdGpo.content["tasks"] += '<ScheduledTasks clsid="{CC63F200-7309-4ba0-B154-A71CD118DBCC}">'
    $createdGpo.content["tasks"] += [environment]::NewLine
    $createdGpo.content["tasks"] += '<TaskV2 clsid="{D8896631-B747-47a7-84A6-C155337F3BC8}" name="'+$createdGpo.name+'" image="2" changed="'+$whenChanged+'" uid="{'+$((new-guid).guid.toupper())+'}" userContext="0" removePolicy="1">'
    $createdGpo.content["tasks"] += [environment]::NewLine
    $createdGpo.content["tasks"] += '<Properties name="'+$createdGpo.name+'" logonType="S4U" runAs="NT AUTHORITY\System" action="R">{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Task version="1.2">{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<RegistrationInfo>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Author>Microsoft Services</Author>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Description></Description>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '</RegistrationInfo>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Principals>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Principal id="Author">{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<UserId>NT AUTHORITY\System</UserId>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<LogonType>S4U</LogonType>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<RunLevel>HighestAvailable</RunLevel>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '</Principal>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '</Principals>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Settings>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<IdleSettings>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Duration>PT10M</Duration>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<WaitTimeout>PT1H</WaitTimeout>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<StopOnIdleEnd>false</StopOnIdleEnd>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<RestartOnIdle>false</RestartOnIdle>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '</IdleSettings>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<AllowHardTerminate>false</AllowHardTerminate>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<AllowStartOnDemand>true</AllowStartOnDemand>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Enabled>true</Enabled>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Hidden>false</Hidden>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<ExecutionTimeLimit>PT1H</ExecutionTimeLimit>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Priority>7</Priority>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '</Settings>{0}' -f [environment]::NewLine
    # triggers
    $createdGpo.content["tasks"] += '<Triggers>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<RegistrationTrigger>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Enabled>true</Enabled>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '</RegistrationTrigger>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '</Triggers>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Actions Context="Author">{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Exec><Command>PowerShell.exe</Command><Arguments>-ExecutionPolicy Bypass -command "&amp; {$qus = (Get-ChildItem -Path HKLM:\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall | Get-ItemProperty | Where-Object {$_.DisplayName -match '+"'"+'Azure Advanced Threat Protection Sensor'+"'"+' }).QuietUninstallString;$sb = [System.Text.StringBuilder]::new();$sb.Append('+"'"+'cmd /c '+"'"+');$sb.Append($([char]39));$sb.Append($([char]34));$sb.append(($qus -split $([char]34))[1]);$sb.Append($([char]34));$sb.Append($([char]32));$sb.append(($qus -split $([char]34))[2]);$sb.Append($([char]39));Invoke-Expression $sb.Tostring()  | out-file c:\windows\temp\mdiremove.log}"</Arguments><WorkingDirectory>C:\Windows\System32</WorkingDirectory></Exec>'
    $createdGpo.content["tasks"] += [environment]::NewLine
    $createdGpo.content["tasks"] += '</Actions></Task>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '</Properties>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<Filters>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '<FilterRegistry bool="AND" not="0" type="KEYEXISTS" hive="HKEY_LOCAL_MACHINE" key="SYSTEM\CurrentControlSet\Services\AATPSensor" valueName="" valueType="" valueData="" min="0.0.0.0" max="0.0.0.0" gte="1" lte="0"/></Filters>'
    $createdGpo.content["tasks"] += [environment]::NewLine
    $createdGpo.content["tasks"] += '</TaskV2>{0}' -f [environment]::NewLine
    $createdGpo.content["tasks"] += '</ScheduledTasks>{0}' -f [environment]::NewLine
    try {
        write-output ("Setting tasks content of GPO $($createdGpo.name)")
        # set gpt second
        if ($myDomain.SetGpoContent($createdGpo.filePath["tasks"],('{0}' -f $createdGpo.content["tasks"]),"UTF8")) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting tasks policy content of GPO $($createdGpo.name) $($createdGpo.filePath["files"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set tasks content of GPO $($createdGpo.name) $($createdGpo.filePath["tasks"]) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set tasks policy content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("scheduledtasks"))) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
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
function Set-MdiSamrGpo {
    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$true)]
        $createdGpo,
        [parameter(Mandatory=$true)]
        [string]$identity,
        [Parameter(Mandatory=$true)]
        $myDomain
    )

    # if the service account name has a $ it's gmsa
    # get the SID of the MDI service account
    $adObject = $mydomain.GetObjectByFilter("|(samaccountname=$identity)(samaccountname=$($identity)$)","domain")
    if ([string]::IsNullOrEmpty($adObject)) {
        $adObject = $mydomain.GetObjectByFilter("|(samaccountname=$identity)(samaccountname=$($identity)$)","forest")
    }
    if (-not [string]::IsNullOrEmpty($adObject)) {
        $stringSid = '*{0}' -f $($myDomain.GetStringSidFromBytes([byte[]]$($adObject.properties.objectsid)))
    }
    
    $createdGpo.filePath.Add("gpt", $($myDomain.writableSysvolPath)+'\Policies\{'+$createdGpo.guid+'}\Machine\microsoft\windows nt\SecEdit\GptTmpl.inf')

    # build block for MDI-All GPT Policies (GPT settings only)
    # we don't need the gpo header unless it's a standard GPO
    $gpoFileHeader = New-GpoHeader
    $createdGpo.content.Add("gpt", $gpoFileHeader)
    $createdGpo.content["gpt"] += [environment]::NewLine
    $createdGpo.content["gpt"] += '[Registry Values]{0}' -f [environment]::NewLine
    $createdGpo.content["gpt"] += 'MACHINE\System\CurrentControlSet\Control\Lsa\RestrictRemoteSAM=1,"O:BAG:BAD:(A;;RC;;;BA)(A;;RC;;;{0})"' -f $stringSid

    try {
        write-output ("Setting content of GPO $($createdGpo.name)")
        # set gpt first
        if ($myDomain.SetGpoContent($createdGpo.filePath["gpt"],('{0}' -f $createdGpo.content["gpt"]),"unicode")) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting policy content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set policy content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set policy content of GPO $($createdGpo.name) $($createdGpo.filePath["gpt"]) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGpcExtension($createdGpo.guid,@("security"))) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting GPCMachineExtension of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set GPCMachineExtension $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    try {
        if ($myDomain.SetGpoGptVersion($createdGpo.guid)) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting version number of GPO $($createdGpo.name), GUID: $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set version number of GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    # set ACLs for domain controllers
    try {
        $dcGroupName = $mydomain.GetObjectBySid("$($mydomain.domainsid)-516","domain").properties["samaccountname"]
        if ($myDomain.AddGpoApplyAcl($($createdGpo.guid),"$dcGroupName","Deny","Domain")) {
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Setting ACL's on GPO for $dcGroupName on GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn)." -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        write-error "Failed to set ACL for Domain Controllers `'$dcGroupName`' on GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). See $env:temp\mircatmdi.log for details."
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set ACL for Enterprise Read Only Domain Controllers on GPO $($createdGpo.name) $($createdGpo.guid) in domain $($myDomain.domainFqdn). $errorMessage" -logSev "Error" | out-null
    }
    ### new section for WMI filter
    # get the GUID if it's there
    try {
        $wmiFilterGuid = $myDomain.GetWmiFilter("Tier 0 - No DC Apply","domain")
        if ($wmiFilterGuid) {
            $wmiFilterGuid = $wmiFilterGuid.id.trim('}').trim('{')
            write-output "Found existing WMI Filter: Tier 0 - No DC Apply"
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Found existing WMI Filter: Tier 0 - No DC Apply. GUID: $wmiFilterGuid" -logSev "Info" | out-null
        } else {
            throw
        }
    } catch {
        $wmiFilterGuid = $myDomain.NewWmiFilter("Tier 0 - No DC Apply","Tier 0 - Used to prevent policy from applying to a domain controller","root\CIMv2",'Select * from Win32_ComputerSystem where DomainRole < 4')
        write-output 'Creating WMI Filter: Tier 0 - No DC Apply'
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage 'Creating WMI Filter: Tier 0 - No DC Apply' -logSev "Info" | out-null
    }
    if ($wmiFilterGuid) {
        start-sleep 3
        try {
            $filterSetCheck = $myDomain.SetWmiFilter($createdGpo.guid,$wmiFilterGuid)
            if ($filterSetCheck) {
                write-output "Set filter to GPO: Tier 0 - No DC Apply"
                Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Set filter to GPO: Tier 0 - No DC Apply" -logSev "Info" | out-null
            } else {
                throw
            }
        }
        catch {
            write-error "Failed to set WMI filter to GPO: Tier 0 - No DC Apply"
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to set filter to GPO: Tier 0 - No DC Apply. $($_.Exception)" -logSev "Error" | out-null
        }
    } else {
        write-error "Failed to create WMI filter: Tier 0 - No DC Apply"
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to create WMI filter: Tier 0 - No DC Apply" -logSev "Error" | out-null
    }
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
function Show-MircatLogs {
    [CmdletBinding()]
    Param(

    )

    $logLocationArray = @(
        "$env:temp\adalops.log",
        "$env:temp\claw.log",
        "$env:temp\mircatmdi.log"
        "$env:systemroot\temp\adalops.log"
        "$env:systemroot\temp\mircatmdi.log"
        #"$env:systemroot\temp\mss\mdisetup\*.log"
    )
    foreach ($path in $logLocationArray) {
        if (Test-Path $path) {
            $logContent += Get-Content $path | ConvertFrom-Csv -Delimiter `t -Header "Date","Severity","Message" | Select-Object *,@{Name='File Name';Expression={"$path"}}
        }
    }
    $logContent | Out-GridView -Title "MIRCAT Logs"
}
function Start-ClawModelCreate
{
    ##########################################################################################################
    <#
    .SYNOPSIS
        Check for and install ALL CLAW components.
    
    .DESCRIPTION
        Checks installation status. If status is not 'Fresh Install' prompts to reset status and then attempts
        to reconfigure all CLAW components, in correct sequence.

    .EXAMPLE
        Start-ClawModelCreate

        Installs all CLAW components, in the correct sequence, for the currently logged in domain.

        Start-ClawModelCreate -domain child.contoso.com

        Installs all CLAW components, in the correct sequence, for the specified domain.
    .OUTPUTS
        Verbose output to host.
        General log written to '$env:temp\claw.log'

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

    ###################################
    ## Function Options and Parameters
    ###################################

    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        #The target domain
        [parameter(Mandatory=$false)]
        [String]$Domain,
        [parameter(Mandatory=$false)]
        [switch]$FullTier,
        [Parameter(Mandatory=$false)]
        [string]$IDOUName="SITH",
        [parameter(Mandatory=$false)]
        [switch]$ForestTier,
        [parameter(Mandatory=$false)]
        [System.Management.Automation.PSCredential]$Credential = [System.Management.Automation.PSCredential]::Empty
    )
    
    $os=[System.Environment]::OSVersion.Version.tostring()
    try { 
        $moduleVersion = (get-module -name mircat).version.tostring()
    } catch {}
    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Entering function Start-ClawModelCreate" -logSev "Info" | out-null
    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "OS $os`: Online. System $($env:computername)`: Online. PowerShell $($psversiontable.psversion.tostring())`: Online. Module $moduleVersion`: Online. All systems nominal." -logSev "Info" | out-null
    write-verbose "Entering function Start-ClawModelCreate"
    Write-Clawv3Logo
    start-sleep 1
    write-output "OS $os`:Online. System $($env:computername)`:Online. PowerShell $($psversiontable.psversion.tostring())`:Online. Module $moduleVersion`:Online. All systems nominal."
    write-output "Doing domain discovery..."
    try {
        $domainCheck= get-wmiobject win32_computersystem
    } catch {}
    if ($($env:USERDNSDOMAIN) -eq $null) {
        $userDomain = "NULL"
    } else {
        $userDomain = $env:USERDNSDOMAIN
    }
    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Doing domain discovery. Computer domain is $($domainCheck.Domain). User domain is $userDomain" -logSev "Info" | out-null
    try {
        if ($Credential -eq [System.Management.Automation.PSCredential]::Empty) {
            $myDomain = [Adal]::new()
             
        } else {
            $myDomain = [Adal]::new($Credential)  
        }
        if (!($Domain)) {
            # we don't have a domain but we got creds
            # let the auto discovery sort that out
            $myDomain.AutoDomainDiscovery($null)
        } else {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Domain argument was provided with value $domain" -logSev "Info" | out-null
            $myDomain.AutoDomainDiscovery("$Domain")
        }
        if ([string]::IsNullOrEmpty($myDomain.chosenDc)) {
            throw "Failed to discover AD"
        }
        if (!($myDomain.writableSysvolPath)) {
            throw "Failed to discover sysvol"
        }
    } catch {
        write-error "$_"
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "$_" -logSev "Error" | out-null
    } finally {

    }

    if (-not [string]::IsNullOrEmpty($myDomain.chosenDc)) { 
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "AD Discovery complete $($myDomain.ToString())" -logSev "Info" | out-null
        if ($FullTier) {
            $title    = 'Confirm Full Tier'
            $question = 'Full tier should not be used in IR engagements. Are you sure?'
            $choices  = '&Yes', '&No'

            $decision = $Host.UI.PromptForChoice($title, $question, $choices, 1)
            if ($decision -eq 0) {
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "User was prompted for full tier confirmation and selected Yes. FullTier = $($FullTier)" -logSev "Info" | out-null
            } else {
                $FullTier = $false
                Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "User was prompted for full tier confirmation and selected No. FullTier = $($FullTier)" -logSev "Info" | out-null
            }
        }

        if ([string]::IsNullOrEmpty($myDomain.writableSysvolPath)) {
            write-error "Failed to discover SYSVOL. See $env:temp\claw.log for details. Critical stop!"
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to discover SYSVOL $($_.Exception)" -logSev "Error" | out-null
            throw
        }
        ### RSAT is now checked for during domain discovery
        if ($ForestTier) {
            $domainList = [System.Collections.Generic.List[AdAl]]::new()
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Running in FOREST tiering mode" -logSev "Info" | out-null
            $domainList = Get-DomainsInForestAsAdalList
            ### foreach DC do the OU's
            foreach ($dom in $domainList){
                New-ClawOu -IDOUName $IDOUName -myDomain $dom
            }
            ### foreach DC do the groups
            foreach ($dom in $domainList){
                New-ClawGroup -IDOUName $IDOUName -myDomain $dom
            }
            ### foreach DC do the GPO's
            foreach ($dom in $domainList){
                New-ClawGpo -FullTier:$FullTier -IDOUName $IDOUName -myDomain $dom -gposToCreate All
            }
        } else {
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Running in single domain tiering mode" -logSev "Info" | out-null
            write-output "$($myDomain.sysvolReplicationInfo)"
            Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "$($myDomain.sysvolReplicationInfo)" -logSev "Info" | out-null
            
            New-ClawOu -IDOUName $IDOUName -myDomain $myDomain
            New-ClawGroup -IDOUName $IDOUName -myDomain $myDomain
            New-ClawGpo -FullTier:$FullTier -IDOUName $IDOUName -myDomain $myDomain -gposToCreate All
        }
        
    } else {
        write-error "Failed to discover AD. See $env:temp\claw.log for details. Critical stop!"
        Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Failed to discover AD. Attempted to contact domain controller in $env:userdomain" -logSev "Error" | out-null
    }
    Add-LogEntry -logFilePath $env:temp\claw.log -logMessage "Finished function Start-ClawModelCreate" -logSev "Info" | out-null
    write-host "======Log file located at $env:temp\claw.log======" -ForegroundColor darkmagenta -BackgroundColor white
}
function Test-IsDC {
    if ( [bool](Get-WmiObject -Query "select * from Win32_OperatingSystem where ProductType='2'") ) {
        $isDc=$true
    } else {
        $isDc=$false
    }
    return $isDc
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
function Test-MDIKDSRootKey {
    [CmdletBinding()]
    Param(
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )
    
    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
    $kdsPath = 'AD:\CN=Master Root Keys,CN=Group Key Distribution Service,CN=Services,CN=Configuration,{0}' -f $myDomain.forestDn
    try {
        $kdsGci = Get-ChildItem -Path $kdsPath
        $kdsGet = (Get-KdsRootKey)
    } catch {
        return $false
    }
    return ($kdsGci.distinguishedName.length -gt 0) -or $kdsGet
}
function Test-MDIRemoteReadiness {
    ##########################################################################################################
    <#
    .SYNOPSIS
        Checks server for MDI remote readiness.

    .DESCRIPTION
        Checks server for MDI remote readiness.

    .EXAMPLE
        Test-MDIRemoteReadiness
        This gets the local server

        Test-MDIRemoteReadiness -allDomainControllers
        This gets all domain controllers

        Test-MDIRemoteReadiness -server myDC1
        This gets the server myDC1

        Test-MDIRemoteReadiness -server myDC1,myDC2,myDC3
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
        [string[]]$server=$(Get-WmiObject win32_computersystem | %{ $_.DNSHostName + "." + $_.Domain}),
        [Parameter(ParameterSetName="allDCs")]
        [switch]$allDomainControllers,
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )
    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain 
    $returnRemoteReadiness = [System.Collections.Generic.List[MdiRemoteReadiness]]::new()
    $actionableServers = @()
    if ($PSCmdlet.ParameterSetName -eq 'server') {
        if ($server -match ',') {
            $actionableServers += ($server.split(','))
        } else {
            if ($server.count -gt 1) {
                $server | % { 
                    $actionableServers += $_
                }
            } else {
                $actionableServers += $server
            }
        }
    }

    
    if ($PSCmdlet.ParameterSetName -eq 'allDCs') {
        $myDomain.domainDetail.ReplicaDirectoryServers | % { 
            $actionableServers += $_
        }
    }
    foreach ($actionableServer in $actionableServers) {
        [MdiRemoteReadiness]$actionableServerMdiRemoteReadiness = [MdiRemoteReadiness]::new($actionableServer, $myDomain)
        try {
            $actionableServerMdiRemoteReadiness.TestAllReadiness()
        } catch {

        } finally {
            $returnRemoteReadiness.Add($actionableServerMdiRemoteReadiness)
        }
    }
    return $returnRemoteReadiness
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
function Test-MDIUserInEnterpriseAdmins {
    [CmdletBinding()]
    Param(
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )
    $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
    $returnVal = $false
    try {
        $id = [Security.Principal.WindowsIdentity]::GetCurrent()
        $groups = $id.Groups | ForEach-Object { try {$_.Translate([Security.Principal.NTAccount])} catch {} }
        $eaGroupName = '{0}\{1}' -f ($myDomain.forestNetbiosName, $($myDomain.privilegedGroupNames["Enterprise Admins"]))
        $returnVal = ($eaGroupName -in $groups)
    } catch {
        $returnVal = $false
    }
    return $returnVal
}
function Update-ADFSCertificateIR
{
  # This script will renew the ADFS Token Signing and Token Decrypting certificates 
  # and restart the ADFS service on all nodes in the farm.

  # This command will only work on the primary ADFS node
  try {
    $dkm = (get-adfsproperties).CertificateSharingContainer
  }
  catch {
    write-error -message "Failed to get ADFS properties, are you running this on the primary ADFS node?"
    Exit
  }

  Write-host "Found DKM container: $dkm" -ForegroundColor Yellow
  $DKMContainer = ([ADSI]"LDAP://$dkm")
  Write-Host "Deleting all objects in $dkm" -ForegroundColor Yellow
  foreach ($object in $DKMContainer.psbase.Children)
  {
    try {
      $object.DeleteTree()
    }
    catch {
      Write-Error -Message "Failed to delete object $($object.Name)"
    }
  }

  Write-host "Creating new ADFS signing certificates" -ForegroundColor Yellow	
  Try {
    Update-AdfsCertificate -CertificateType Token-Signing -Urgent
    Update-AdfsCertificate -CertificateType Token-Decrypting -Urgent
  } catch {
    write-error -message "Failed to create new ADFS signing certificates"
    Exit
  }

  # Restart ADFS Service on all nodes
  write-host "Restarting ADFS service on all nodes" -ForegroundColor Yellow
  foreach ($node in (Get-AdfsFarmInformation).farmnodes)
  {
    Try {
      write-host "Restarting ADFS service on $node" -ForegroundColor Yellow
      Invoke-Command -ComputerName $node.FQDN -ScriptBlock {Restart-Service adfssrv -Force}
    } catch {
      write-error -message "Failed to restart ADFS service on $node"
    }
  }
}
function Update-ADFSWIDPermissions
{
  [CmdletBinding()]
  param (
      # Must be in the format domain\username
      [Parameter()]
      [string]
      $principalName,

      [Parameter()]
      [switch]
      $dump,

      [Parameter()]
      [switch]
      $removePrincipal,

      [Parameter()]
      [switch]
      $addPrincipal,

      [Parameter()]
      [string]
      $SQLServer = "\\.\pipe\microsoft##wid\tsql\query"

  )

  if ($dump)
  {
    $sqlquery = "sp_helplogins;"
  } elseIf ($removePrincipal)
  {
    $sqlquery = "sp_droplogin [$principalName];"
  } elseif ($addPrincipal)
  {
    $sqlquery = "sp_grantlogin [$principalName];"
  }


  $SqlConnection = New-Object System.Data.SqlClient.SqlConnection
  $SqlConnection.ConnectionString = "Server = $SQLServer; Integrated Security = True;"

  $SqlCmd = New-Object System.Data.SqlClient.SqlCommand
  $SqlCmd.CommandText = $sqlquery
  $SqlCmd.Connection = $SqlConnection

  if ($dump)
  {
    $SqlAdapter = New-Object System.Data.SqlClient.SqlDataAdapter
    $SqlAdapter.SelectCommand = $SqlCmd
    $DataSet = New-Object System.Data.DataSet
    $SqlAdapter.Fill($DataSet)
    $DataSet.Tables[0]
  }

  If ($addPrincipal -or $removePrincipal)
  {
    $SqlConnection.Open()
    $SqlCmd.ExecuteNonQuery()
    $SqlConnection.Close()
  }
}
function Update-EntraDomainFederation
{
  <#
    .SYNOPSIS
    Updates or manages federation between an ADFS server and an Entra ID custom domain using Microsoft Graph API.

    .DESCRIPTION
    This cmdlet manages federation between ADFS and Entra ID (formerly Azure AD) by retrieving federation metadata and configuring the appropriate trusts.
    It supports multiple operation modes: updating an existing federation trust, creating a new federation, creating only an ADFS relying party, or removing federation.

    .PARAMETER adfs_url
    The URL of the ADFS server (e.g., sts.contoso.com).

    .PARAMETER EntraCustomDomainName
    The validated custom domain in Entra to federate.

    .PARAMETER mode
    The operation mode. Valid values are:
    - updateEntraDomainTrust: Updates an existing federation trust
    - newDomainFederation: Creates a new federation for the domain
    - onlyCreateADFSRP: Only creates the ADFS relying party trust without modifying Entra ID
    - removeFederation: Removes the federation for the specified domain

    .PARAMETER adfsprimarynode
    The ADFS server hostname (e.g., adfs.contoso.local). Required when mode is newDomainFederation or onlyCreateADFSRP.

    .EXAMPLE
    Update-EntraDomainFederation -adfs_url "sts.contoso.com" -EntraCustomDomainName "contoso.com" -mode "updateEntraDomainTrust"

    .EXAMPLE
    Update-EntraDomainFederation -adfs_url "sts.contoso.com" -EntraCustomDomainName "contoso.com" -mode "newDomainFederation" -adfsprimarynode "adfs.contoso.local"

    .EXAMPLE
    Update-EntraDomainFederation -adfs_url "sts.contoso.com" -EntraCustomDomainName "contoso.com" -mode "removeFederation"

    .NOTES
    For multi-domain federation, use Entra ID Connect Wizard instead of this cmdlet
  #>


  [CmdletBinding()]
  param (
      [Parameter(Mandatory = $true)]
      [string]
      $adfs_url, # e.g. sts.contoso.com

      [Parameter(Mandatory = $true)]
      [string]
      $EntraCustomDomainName, # the validated custom domain in Entra to federate

      [Parameter(Mandatory = $true)]
      [ValidateSet("updateEntraDomainTrust", "newDomainFederation", "onlyCreateADFSRP", "removeFederation")]
      [string]
      $mode,

      [Parameter()]
      [string]
      $adfsprimarynode # ADFS server hostname e.g. adfs.contoso.local
    )

  if((($mode -eq "newDomainFederation") -or ($mode -eq "onlyCreateADFSRP")) -and [string]::IsNullOrEmpty($adfsprimarynode))
  {
    Write-Error "The -adfsprimarynode parameter is required when creating a new trust" -ErrorAction Stop
  }

  $EntraDomains = [graphal]::new(@{objectType="domain"})
  
  #region get federation metadata
  Function Get-ADFSMetadata($adfs_url)
  {
    #$metadataurl = "https://login.windows.net/contoso.onmicrosoft.com/federationmetadata/2007-06/federationmetadata.xml?Appid=<GUID>"
    #$metadataurl = "https://sts.contoso.com/FederationMetadata/2007-06/FederationMetadata.xml"

    $metadataurl = "https://$adfs_url/FederationMetadata/2007-06/FederationMetadata.xml"

    Write-Host "Retrieving federation metadata" -ForegroundColor Magenta
    Try {
      $utfstring = Invoke-WebRequest $metadataurl -ContentType "text/xml; charset=utf-8" | Select-Object -expandproperty content
    } Catch { 
      Write-Error "Unable to retrieve metadata from $metadataurl" -ErrorAction Stop
    }

    [xml]$xmldata = [text.encoding]::utf8.getstring([text.encoding]::default.GetBytes($utfstring))

    write-host "Parsing metadata" -ForegroundColor Magenta
    $STSMetadata = [PSCustomObject]@{
      tokensignincert = $xmldata.EntityDescriptor.Signature.keyinfo.X509Data.X509Certificate
      ActiveSignInUri = $xmldata.EntityDescriptor.entityID + "/usernamemixed" #Guestimation: "https://sts.contoso.com/adfs/services/trust/2005/usernamemixed" 
      DisplayName = $xmldata.EntityDescriptor.RoleDescriptor.Servicedisplayname[0]
      IssuerUri = $xmldata.EntityDescriptor.entityID
      MetadataExchangeUri = $xmldata.EntityDescriptor.RoleDescriptor.SecurityTokenServiceEndpoint.EndpointReference.metadata.metadata.metadatasection.metadatareference.address."#text"
      PassiveSignInUri = $xmldata.EntityDescriptor.RoleDescriptor.passiverequestorendpoint.endpointreference[0].address
      SignOutUri = $xmldata.EntityDescriptor.RoleDescriptor.passiverequestorendpoint.endpointreference[0].address
    }

    Return $STSMetadata
  }
  #endregion get federation metadata

  #region Get-entradomain
  Function Get-Entradomain($EntraCustomDomainName)
  {
    # Check if the custom domain exists
    $entraDomainList =  $entraDomains.get(@{}).value.id
    If ($entraDomainList -notcontains $EntraCustomDomainName)
    {
      Write-Error "The domain $EntraCustomDomainName does not exist in Azure AD" -ErrorAction stop
    } else {
      $domain = $entraDomains.get(@{objectid=$EntraCustomDomainName})
    }
    Return $domain
  }
  #endregion get-entradomain

  #region update trust
  if ($mode -eq "updateEntraDomainTrust")
  {
    $domain = Get-Entradomain($EntraCustomDomainName)

    # check if custom domain is federated
    if ($domain.authenticationType -ne "Federated")
    {
      Write-Error "The domain $EntraCustomDomainName is not federated" -ErrorAction stop
    }

    $STSMetadata = Get-ADFSMetadata $adfs_url

    #$internalDomainFederationId = (Get-MgDomainFederationConfiguration -DomainId $EntraCustomDomainName).id
    $DomainFedConfig = $EntraDomains.invoke(@{method="GET"; uri="$EntraCustomDomainName/federationConfiguration"}).value

    # SupportsMultipleDomain logic (If same STS is used for multiple domains)
    if ($DomainFedConfig.IssuerUri -match [regex]::escape('http://' + $EntraCustomDomainName + '/adfs/services/trust/'))
    {
      # SupportsMultipleDomain
      Write-host "SupportsMultipleDomain is set" -ForegroundColor Magenta
      $issuerUri = 'http://' + $EntraCustomDomainName + '/adfs/services/trust/'
    } Else {
      $issuerUri = $STSMetadata.IssuerUri
    }

    $internalDomainFederationId = $DomainFedConfig.id

    Write-Host "Updating federation trust for domain $EntraCustomDomainName" -ForegroundColor Magenta
    try {
      $fedparams = @{
        "activeSignInUri" = $STSMetadata.ActiveSignInUri
        "displayName" = $STSMetadata.DisplayName
        "issuerUri" = $issuerUri
        "metadataExchangeUri" = $STSMetadata.MetadataExchangeUri
        "passiveSignInUri" = $STSMetadata.PassiveSignInUri
        "signOutUri" = $STSMetadata.SignOutUri
        "signingCertificate" = $STSMetadata.tokensignincert
        "federatedIdpMfaBehavior" = "acceptIfMfaDoneByFederatedIdp"
      }

      $entraDomains.invoke(@{method="patch"; uri=($EntraCustomDomainName,"federationConfiguration",$internalDomainFederationId -join "/"); body=$fedparams})
    } catch {
      Write-Error "Unable to update federation configuration for $EntraCustomDomainName`: " `
        $_.Exception.Message -ErrorAction Stop
    }
  }
  #endregion update trust

  #region create new trust
  if (($mode -eq "newDomainFederation") -or ($mode -eq "onlyCreateADFSRP"))
  {
    Write-Warning -Message "This procedure will not support federating multiple custom domains to a single ADFS farm. To configure this, please use the Entra ID Connect Wizard to configure the federation."

    $djoined = try{Test-ComputerSecureChannel}catch{}

    if ($djoined -ne $true)
    {
      Write-Host "Not domain joined" -ForegroundColor Magenta
      $cred = (Get-Credential -Message "Enter credentials for ADFS server")
    }

    # check if we can connect to the adfs server
    if (-not(Test-WSMan -ComputerName $adfsprimarynode -ErrorAction SilentlyContinue))
    {
      Write-Error -Message "Unable to connect to ADFS server $adfsprimarynode" -ErrorAction Stop
#      exit
    }

    $params = @{
                ScriptBlock = {
                  # The following rules do NOT support a multi-domain trust. 
                  # https://learn.microsoft.com/en-us/entra/identity/hybrid/connect/how-to-connect-install-multiple-domains#multiple-top-level-domain-support
                  # To configure this, please use the Entra ID Connect Wizard to configure the federation.
                  $RuleSet = New-AdfsClaimRuleSet -ClaimRule `
                      "@RuleName = `"Issue UPN`"
                      c:[Type == `"http://schemas.microsoft.com/ws/2008/06/identity/claims/windowsaccountname`"]
                      => issue(store = `"Active Directory`", types = (`"http://schemas.xmlsoap.org/claims/UPN`"), query = `"samAccountName={0};userPrincipalName;{1}`", param = regexreplace(c.Value, `"(?<domain>[^\\]+)\\(?<user>.+)`", `"`${user}`"), param = c.Value);",
                      
                      "@RuleName = `"Issue Immutable ID`"
                      c:[Type == `"http://schemas.microsoft.com/ws/2008/06/identity/claims/windowsaccountname`"]
                      => issue(store = `"Active Directory`", types = (`"http://schemas.microsoft.com/LiveID/Federation/2008/05/ImmutableID`"), query = `"samAccountName={0};objectGUID;{1}`", param = regexreplace(c.Value, `"(?<domain>[^\\]+)\\(?<user>.+)`", `"`${user}`"), param = c.Value);",
                      
                      "@RuleName = `"Issue nameidentifier`"
                      c:[Type == `"http://schemas.microsoft.com/LiveID/Federation/2008/05/ImmutableID`"]
                      => issue(Type = `"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`", Value = c.Value, Properties[`"http://schemas.xmlsoap.org/ws/2005/05/identity/claimproperties/format`"] = `"urn:oasis:names:tc:SAML:1.1:nameid-format:unspecified`");",
                      
                      "@RuleName = `"Issue accounttype for domain-joined computers`"
                      c:[Type == `"http://schemas.microsoft.com/ws/2008/06/identity/claims/groupsid`", Value =~ `"-515`$`", Issuer =~ `"^(AD AUTHORITY|SELF AUTHORITY|LOCAL AUTHORITY)`$`"]
                      => issue(Type = `"http://schemas.microsoft.com/ws/2012/01/accounttype`", Value = `"DJ`");",
                      
                      "@RuleName = `"Issue onpremobjectguid for domain-joined computers`"
                      c1:[Type == `"http://schemas.microsoft.com/ws/2008/06/identity/claims/groupsid`", Value =~ `"-515`$`", Issuer =~ `"^(AD AUTHORITY|SELF AUTHORITY|LOCAL AUTHORITY)`$`"]
                      && c2:[Type == `"http://schemas.microsoft.com/ws/2008/06/identity/claims/windowsaccountname`", Issuer =~ `"^(AD AUTHORITY|SELF AUTHORITY|LOCAL AUTHORITY)`$`"]
                      => issue(store = `"Active Directory`", types = (`"http://schemas.microsoft.com/identity/claims/onpremobjectguid`"), query = `";objectguid;{0}`", param = c2.Value);",
                      
                      "@RuleName = `"Pass through primary SID`"
                      c1:[Type == `"http://schemas.microsoft.com/ws/2008/06/identity/claims/groupsid`", Value =~ `"-515`$`", Issuer =~ `"^(AD AUTHORITY|SELF AUTHORITY|LOCAL AUTHORITY)`$`"]
                      && c2:[Type == `"http://schemas.microsoft.com/ws/2008/06/identity/claims/primarysid`", Issuer =~ `"^(AD AUTHORITY|SELF AUTHORITY|LOCAL AUTHORITY)`$`"]
                      => issue(claim = c2);",
                      
                      "@RuleName = `"Pass through claim - insideCorporateNetwork`"
                      c:[Type == `"http://schemas.microsoft.com/ws/2012/01/insidecorporatenetwork`"]
                      => issue(claim = c);",
                      
                      "@RuleName = `"Pass Through Claim - Psso`"
                      c:[Type == `"http://schemas.microsoft.com/2014/03/psso`"]
                      => issue(claim = c);",
                      
                      "@RuleName = `"Issue Password Expiry Claims`"
                      c1:[Type == `"http://schemas.microsoft.com/ws/2012/01/passwordexpirationtime`"]
                      => issue(store = `"_PasswordExpiryStore`", types = (`"http://schemas.microsoft.com/ws/2012/01/passwordexpirationtime`", `"http://schemas.microsoft.com/ws/2012/01/passwordexpirationdays`", `"http://schemas.microsoft.com/ws/2012/01/passwordchangeurl`"), query = `"{0};`", param = c1.Value);",
                      
                      "@RuleName = `"Pass through claim - authnmethodsreferences`"
                      c:[Type == `"http://schemas.microsoft.com/claims/authnmethodsreferences`"]
                      => issue(claim = c);",
                      
                      "@RuleName = `"Pass through claim - multifactorauthenticationinstant`"
                      c:[Type == `"http://schemas.microsoft.com/ws/2017/04/identity/claims/multifactorauthenticationinstant`"]
                      => issue(claim = c);",
                      
                      "@RuleName = `"Pass through claim - certificate authentication - serial number`"
                      c:[Type == `"http://schemas.microsoft.com/ws/2008/06/identity/claims/serialnumber`"]  => issue(claim = c);",
                      
                      "@RuleName = `"Pass through claim - certificate authentication - issuer`"
                      c:[Type == `"http://schemas.microsoft.com/2012/12/certificatecontext/field/issuer`"]  => issue(claim = c);
                      "
              
                  if (-not(Get-AdfsRelyingPartyTrust -Identifier "urn:federation:MicrosoftOnline"))
                  {
                    Add-AdfsRelyingPartyTrust -name  "Microsoft Office 365 Identity Platform"  `
                      -MetadataUrl "https://nexus.microsoftonline-p.com/federationmetadata/2007-06/federationmetadata.xml" `
                      -MonitoringEnabled $true `
                      -autoUpdateEnabled $true
                  }

                  Set-AdfsRelyingPartyTrust -TargetIdentifier "urn:federation:MicrosoftOnline" -IssuanceTransformRules $RuleSet.ClaimRulesString;
                } #end of scriptblock

                ComputerName = $adfsprimarynode
              }
    
    If ($null -ne $cred)
    {
      $params.Credential = $cred
    }

    write-host "Creating relying party trust on remote server..." -ForegroundColor Magenta
    Try {
      Invoke-Command @params
    } catch {
      Write-Error "Failed to create relying party trust on remote server."
    }
    
    if ($mode -eq "newDomainFederation")
    {
      $domain = Get-entraDomain($EntraCustomDomainName)
      if ($domain.authenticationType -eq "Federated")
      {
        if ($domain.issuerUri -ne $issuerUri)
        {
          Write-Error "The domain $EntraCustomDomainName is already federated with a different issuerUri" -ErrorAction Stop
        } else {
          Write-Error "The domain $EntraCustomDomainName is already federated, run the command with -mode updateEntraDomainTrust to update the federation trust" -ErrorAction Stop
        }
      }

      $STSMetadata = Get-ADFSMetadata $adfs_url

      Write-Host "Converting custom domain $EntraCustomDomainName from managed to federated" -ForegroundColor Magenta
      # https://learn.microsoft.com/en-us/graph/api/domain-post-federationconfiguration?view=graph-rest-1.0&tabs=http
      try {

        $fedparams = @{
          "@odata.type" = "#microsoft.graph.internalDomainFederation"
          "displayName"=  $STSMetadata.DisplayName
          "federatedIdpMfaBehavior" = "acceptIfMfaDoneByFederatedIdp"
          "issuerUri" = $STSMetadata.IssuerUri
          "metadataExchangeUri" = $STSMetadata.MetadataExchangeUri
          "passiveSignInUri" = $STSMetadata.PassiveSignInUri
          "activeSignInUri" = $STSMetadata.ActiveSignInUri
          "preferredAuthenticationProtocol" = "wsFed"
          "signOutUri" = $STSMetadata.SignOutUri
          "signingCertificate" = $STSMetadata.tokensignincert
          "promptLoginBehavior" = "nativeSupport"
          "isSignedAuthenticationRequestRequired" = $true
          "supportsMfa" = $true
        }

        #Invoke-MgGraphRequest -Method POST -Uri "https://graph.microsoft.com/v1.0/domains/$EntraCustomDomainName/federationConfiguration" -Body $fedparams
        $entraDomains.invoke(@{method="POST"; uri=($EntraCustomDomainName,"federationConfiguration" -join "/") ; body=$fedparams})
      } catch {
        Write-Error "Unable to convert domain $EntraCustomDomainName to federated: " `
          $_.Exception.Message
        Exit
      }
    }
  }
  #endregion create new trust

  #region remove federation
  if ($mode -eq "removeFederation")
  {
    $fedId = $entraDomains.invoke(@{method="GET"; uri=($EntraCustomDomainName,"federationConfiguration" -join "/")}).value.id
    $entraDomains.invoke(@{method="DELETE"; uri=($EntraCustomDomainName,"federationConfiguration",$fedId -join "/")})
  }
  #endregion remove federation

  <# adfshelp.com output (for reference only)

      $message = "";
      function Backup-IssuanceTransformRules {
        $currentpath = Resolve-Path .
        $timestamp = Get-Date -f yyyy.MM.dd_hh.mm.ss;
        $filename = "$currentpath\Backup $timestamp.txt";
        $claims = Get-AdfsRelyingPartyTrust -Identifier $(Get-RpIdentifier) | Select-Object IssuanceTransformRules;
        $stream = [System.IO.StreamWriter] $filename
        foreach($claim in $claims.IssuanceTransformRules) {
          $stream.WriteLine($claim);
        }
        $stream.close();
        $script:message = "Backup file with claim rules created: $filename";
      }

      function Get-RpIdentifier {
        [string]$rpidentifier = "";
        $identifiers = Get-AdfsRelyingPartyTrust | Select-Object Identifier;
        foreach($id in $identifiers.Identifier) {
          if($id.IndexOf("microsoftonline", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -and $id.StartsWith("urn", [System.StringComparison]::OrdinalIgnoreCase)) {
            $rpidentifier = $id;
            break;
          }
          
        }

        if([string]::IsNullOrEmpty($rpidentifier)) {
          throw "Unable to get the ADFS relying party trust identifier.";
        }

        return $rpidentifier;
      }

      function Update-AdfsClaimRules {
      $RuleSet = New-AdfsClaimRuleSet -ClaimRule `
        "@RuleName = `"Issue UPN`"
        c:[Type == `"http://schemas.microsoft.com/ws/2008/06/identity/claims/windowsaccountname`"]
        => issue(store = `"Active Directory`", types = (`"http://schemas.xmlsoap.org/claims/UPN`"), query = `"samAccountName={0};userPrincipalName;{1}`", param = regexreplace(c.Value, `"(?<domain>[^\\]+)\\(?<user>.+)`", `"`${user}`"), param = c.Value);",
        
        "@RuleName = `"Query objectguid and msdsconsistencyguid for custom ImmutableId claim`"
        c:[Type == `"http://schemas.microsoft.com/ws/2008/06/identity/claims/windowsaccountname`"]
        => add(store = `"Active Directory`", types = (`"http://schemas.microsoft.com/ws/2016/02/identity/claims/objectguid`", `"http://schemas.microsoft.com/ws/2016/02/identity/claims/msdsconsistencyguid`"), query = `"samAccountName={0};objectGUID,mS-DS-ConsistencyGuid;{1}`", param = regexreplace(c.Value, `"(?<domain>[^\\]+)\\(?<user>.+)`", `"`${user}`"), param = c.Value);",
        
        "@RuleName = `"Check for the existence of msdsconsistencyguid`"
        NOT EXISTS([Type == `"http://schemas.microsoft.com/ws/2016/02/identity/claims/msdsconsistencyguid`"])
        => add(Type = `"urn:federation:tmp/idflag`", Value = `"useguid`");",
        
        "@RuleName = `"Issue msdsconsistencyguid as Immutable ID if it exists`"
        c:[Type == `"http://schemas.microsoft.com/ws/2016/02/identity/claims/msdsconsistencyguid`"]
        => issue(Type = `"http://schemas.microsoft.com/LiveID/Federation/2008/05/ImmutableID`", Value = c.Value);",
        
        "@RuleName = `"Issue objectGuidRule if msdsConsistencyGuid rule does not exist`"
        c1:[Type == `"urn:federation:tmp/idflag`", Value =~ `"useguid`"]
        && c2:[Type == `"http://schemas.microsoft.com/ws/2016/02/identity/claims/objectguid`"]
        => issue(Type = `"http://schemas.microsoft.com/LiveID/Federation/2008/05/ImmutableID`", Value = c2.Value);",
        
        "@RuleName = `"Issue nameidentifier`"
        c:[Type == `"http://schemas.microsoft.com/LiveID/Federation/2008/05/ImmutableID`"]
        => issue(Type = `"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`", Value = c.Value, Properties[`"http://schemas.xmlsoap.org/ws/2005/05/identity/claimproperties/format`"] = `"urn:oasis:names:tc:SAML:1.1:nameid-format:unspecified`");",
        
        "@RuleName = `"Issue accounttype for domain-joined computers`"
        c:[Type == `"http://schemas.microsoft.com/ws/2008/06/identity/claims/groupsid`", Value =~ `"-515`$`", Issuer =~ `"^(AD AUTHORITY|SELF AUTHORITY|LOCAL AUTHORITY)`$`"]
        => issue(Type = `"http://schemas.microsoft.com/ws/2012/01/accounttype`", Value = `"DJ`");",
        
        "@RuleName = `"Issue AccountType with the value USER when it is not a computer account`" NOT EXISTS([Type == `"http://schemas.microsoft.com/ws/2012/01/accounttype`", Value == `"DJ`"])
        => add(Type = `"http://schemas.microsoft.com/ws/2012/01/accounttype`", Value = `"User`");",
        
        "@RuleName = `"Issue issuerid when it is not a computer account`" c1:[Type == `"http://schemas.xmlsoap.org/claims/UPN`"] && c2:[Type == `"http://schemas.microsoft.com/ws/2012/01/accounttype`", Value == `"User`"] => issue(Type = `"http://schemas.microsoft.com/ws/2008/06/identity/claims/issuerid`", Value = regexreplace(c1.Value, `"(?i)(^([^@]+)@)(?<domain>(contoso\.com|sdprop\.nl))`$`", `"http://`${domain}/adfs/services/trust/`"));",
        
        "@RuleName = `"Issue issuerid for DJ computer auth`" c1:[Type == `"http://schemas.microsoft.com/ws/2012/01/accounttype`", Value == `"DJ`"] => issue(Type = `"http://schemas.microsoft.com/ws/2008/06/identity/claims/issuerid`", Value =`"http://contoso.com/adfs/services/trust/`");",
        
        "@RuleName = `"Issue onpremobjectguid for domain-joined computers`"
        c1:[Type == `"http://schemas.microsoft.com/ws/2008/06/identity/claims/groupsid`", Value =~ `"-515`$`", Issuer =~ `"^(AD AUTHORITY|SELF AUTHORITY|LOCAL AUTHORITY)`$`"]
        && c2:[Type == `"http://schemas.microsoft.com/ws/2008/06/identity/claims/windowsaccountname`", Issuer =~ `"^(AD AUTHORITY|SELF AUTHORITY|LOCAL AUTHORITY)`$`"]
        => issue(store = `"Active Directory`", types = (`"http://schemas.microsoft.com/identity/claims/onpremobjectguid`"), query = `";objectguid;{0}`", param = c2.Value);",
        
        "@RuleName = `"Pass through primary SID`"
        c1:[Type == `"http://schemas.microsoft.com/ws/2008/06/identity/claims/groupsid`", Value =~ `"-515`$`", Issuer =~ `"^(AD AUTHORITY|SELF AUTHORITY|LOCAL AUTHORITY)`$`"]
        && c2:[Type == `"http://schemas.microsoft.com/ws/2008/06/identity/claims/primarysid`", Issuer =~ `"^(AD AUTHORITY|SELF AUTHORITY|LOCAL AUTHORITY)`$`"]
        => issue(claim = c2);",
        
        "@RuleName = `"Pass through claim - insideCorporateNetwork`"
        c:[Type == `"http://schemas.microsoft.com/ws/2012/01/insidecorporatenetwork`"]
        => issue(claim = c);",
        
        "@RuleName = `"Pass Through Claim - Psso`"
        c:[Type == `"http://schemas.microsoft.com/2014/03/psso`"]
        => issue(claim = c);",
        
        "@RuleName = `"Issue Password Expiry Claims`"
        c1:[Type == `"http://schemas.microsoft.com/ws/2012/01/passwordexpirationtime`"]
        => issue(store = `"_PasswordExpiryStore`", types = (`"http://schemas.microsoft.com/ws/2012/01/passwordexpirationtime`", `"http://schemas.microsoft.com/ws/2012/01/passwordexpirationdays`", `"http://schemas.microsoft.com/ws/2012/01/passwordchangeurl`"), query = `"{0};`", param = c1.Value);",
        
        "@RuleName = `"Pass through claim - authnmethodsreferences`"
        c:[Type == `"http://schemas.microsoft.com/claims/authnmethodsreferences`"]
        => issue(claim = c);",
        
        "@RuleName = `"Pass through claim - multifactorauthenticationinstant`"
        c:[Type == `"http://schemas.microsoft.com/ws/2017/04/identity/claims/multifactorauthenticationinstant`"]
        => issue(claim = c);",
        
        "@RuleName = `"Pass through claim - certificate authentication - serial number`"
        c:[Type == `"http://schemas.microsoft.com/ws/2008/06/identity/claims/serialnumber`"]  => issue(claim = c);",
        
        "@RuleName = `"Pass through claim - certificate authentication - issuer`"
        c:[Type == `"http://schemas.microsoft.com/2012/12/certificatecontext/field/issuer`"]  => issue(claim = c);"

        
      Backup-IssuanceTransformRules;
      Set-AdfsRelyingPartyTrust -TargetIdentifier $(Get-RpIdentifier) -IssuanceTransformRules $RuleSet.ClaimRulesString;
      Write-Host $message;
      Write-Host Successfully updated AD FS claim rules;
      }
      Update-AdfsClaimRules;

  #>
}
$script:validGraphObjectTypes = [graphObjectTypePermissions]::new().getAll().Keys

function Update-EntraObject
{
  # Example usage:
  # Update-EntraObject -objectType user -objectId "1d3b3e1f-2b4e-4b1e-8f4d-0f4f1f4f1f4f" -postBody @{accountEnabled = $true; displayName = "Mircat"}

  [CmdletBinding()]
  param
  (
    [Parameter(Mandatory = $true)]
    #[ValidateSet([graphObjectTypeValidator])] # This is not working in powershell 5.0
    [ValidateScript({
      $validValues = [graphObjectTypePermissions]::new().getAll().Keys
      if ($_ -in $validValues) {
          $true
      } else {
          throw "Invalid value '$_'. Valid values are: $($validValues -join ', ')"
      }
    })]
    [string] $objectType,
  
    [Parameter(Mandatory = $true)]
    [string] $objectId,

    [Parameter(Mandatory = $true)]
    [hashtable] $postBody
  )

  $graphCollection = [GraphAL]::new(@{objectType = $objectType})
  $graphCollection.Update(@{objectId = $objectId; postBody = $postBody})
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
function Use-GpoName
{
    param(
        [Parameter(Mandatory)] [string[]] $GpoName,
        [Parameter(Mandatory)] [string[]] $ActionItem
    )
    $ActionItem += 'All'
    @(Compare-Object -ReferenceObject $GpoName -DifferenceObject $ActionItem -ExcludeDifferent -IncludeEqual).Count -gt 0
}
function Use-MdiGraph {
    [CmdletBinding()]
    Param()
    if (-not $([bool](Get-InstalledModule | ? { $_.name -match "Microsoft.Graph.Beta.Security" }))) {
        install-module Microsoft.Graph.Beta.Security -Force -AllowClobber
    }
    try {
        $regIeAdmin = get-itemproperty 'HKLM:\SOFTWARE\Microsoft\Active Setup\Installed Components\{A509B1A7-37EF-4b3f-8CFC-4F3A74704073}' -erroraction silentlycontinue
        $regIeUser = get-itemproperty 'HKLM:\SOFTWARE\Microsoft\Active Setup\Installed Components\{A509B1A8-37EF-4b3f-8CFC-4F3A74704073}' -erroraction silentlycontinue
        $regIeHarden = get-itemproperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings\ZoneMap' -erroraction silentlycontinue
        if ($regIeAdmin.IsInstalled -eq 1) {
            set-itemproperty 'HKLM:\SOFTWARE\Microsoft\Active Setup\Installed Components\{A509B1A7-37EF-4b3f-8CFC-4F3A74704073}' -name "IsInstalled" -value 0 -type DWord
        }
        if ($regIeUser.IsInstalled -eq 1) {
            set-itemproperty 'HKLM:\SOFTWARE\Microsoft\Active Setup\Installed Components\{A509B1A8-37EF-4b3f-8CFC-4F3A74704073}' -name "IsInstalled" -value 0 -type DWord
        }
        if ($regIeHarden.IEHarden -eq 1) {
            Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings\ZoneMap' -Name "IEHarden"
            set-itemproperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings\ZoneMap' -name "IntranetName" -value 1 -type DWord
        }
    } catch {
        write-warning "Unable to disable IE enhanced security."
    }
    Import-Module Microsoft.Graph.Beta.Security
    try {
        Connect-MgGraph -Scopes SecurityIdentitiesSensors.Read.All -NoWelcome -erroraction silentlycontinue
        if (!(Get-MgContext)) { throw }
    } catch {
        write-warning "Graph connection failed!"
    }
    if ($regIeAdmin) {
        set-itemproperty 'HKLM:\SOFTWARE\Microsoft\Active Setup\Installed Components\{A509B1A7-37EF-4b3f-8CFC-4F3A74704073}' -name "IsInstalled" -value $regIeAdmin.IsInstalled -type DWord
    }
    if ($regIeUser) {
        set-itemproperty 'HKLM:\SOFTWARE\Microsoft\Active Setup\Installed Components\{A509B1A8-37EF-4b3f-8CFC-4F3A74704073}' -name "IsInstalled" -value $regIeUser.IsInstalled -type DWord
    }
    if ($regIeHarden) {
        set-itemproperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings\ZoneMap' -name "IEHarden" -value $regIeHarden.IEHarden -type DWord
        set-itemproperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings\ZoneMap' -name "IntranetName" -value $regIeHarden.IntranetName -type DWord
    }
}
function Write-ClawLogo {
    $byline = @'
        __  __ ___ ___  ___   _ _____   ___                    _      
        |  \/  |_ _| _ \/ __| /_|_   _| | _ \_ _ ___ ______ _ _| |_ ___
        | |\/| || ||   | (__ / _ \| |   |  _| '_/ -_(_-/ -_| ' |  _(_-<
        |_|  |_|___|_|_\\___/_/ \_|_|   |_| |_| \___/__\___|_||_\__/__/
'@
    $logo = @'                                                             
                              %@%@@@@@@@@@@@@@@@@                                 
                              %@@@@@@@@@@@@@@@@@%                                 
                              @@@@@@@@@@@@@@@@@@@.                                
                         .=*#@@@@@@@@@%@@@@@@@@@@@#+=:                            
                      -*%@@@@@@%@%@@@@@@@@@@@@@@@@@@@@@*=.                        
                   :*@@@@@@@@%@@%@%@@@@@@@@@@@@@%@@%@@@%@@#=                      
                 -#@@@@%@@@@%@@@@@@@@@@@@@%####@@%@@@@@@@@@@@+.                   
               :#%@@@%@%@@@#=:. -%@@@@@#        .:=#@@@@@@@@@@@+                  
              +%@@@@@@@@#=.      :@%@@@:             :+@@@@@@@@@#:                
            .#@@%@@@@%+:         .@@@%#                 -#%@@@@@@@=               
           .%@@@@@@@=             @@@@=                   :#@@@@@@@=              
           %@@@@@@*.:%%#%#%#######@@@@%=   ++===-%@%%@%@%@@@=%@@@@@@-             
          *@@@%@%:  :%@@@@@@@@@@@@@@@@@+  .@@@@%+@@@@@@@@@@@  *@@%@%@:            
         -@@@@%#.   .----#@@@@#===#@@%@+  .%@@@@+@@@@@+...::   =@@@@@#            
         %@@@@#          +@@@@+   #%@@@%*#%@@@@@+@@@@@%%%+      =@@@@@-           
        -@@@@@.          *%@@%+   #@@@@@@@@@@@@@=@@@@@@%@=       +@@@@#           
        +@@@@-           *@%@@+   %@@@@#-:=@@@@@-%@@@@...         %@@@@           
        *@@@%            #@@@@=   %@@@%*  -@@@@%=@@@@@@@@@@@.     =%@%@.          
        *@@@=            %@@@@+   %%%%%*  -@%@@#=@@@@@@@@@@@       @%@%           
        =@@%::+#%@@%#*=. =****=.          .-@%-:   :-+#@::::..  .%%@@@%           
        .@%@%@@@@@@@@@@@**@@@@@:          :@@@*  .%@@%@@*  =@%- #@@@@@*           
         *@@@@@@@%*=+*%+ *@@@@@.         -@@@@@#  +%@@@@@=%@@@@#@@@@@%.           
         .@@@@@@+        *@@@@@         =@%@%@@@#. #@@@@@@@@@@@@@@@@@+            
          @@@@@@:        #@@@@@        +@%@%*@%@@%.:@@@@@@@@%@@@@@@@#             
          +@%@%@%+:  .   %@@@@%       *%@@@#-%@@@@%.-@@@@@@= :%@@@@*              
           =@@@@@@@@@@*  %@@@@@@@%%@ *@@@@@@@@@@@@@@-*@@%%:   .#@@@.              
            .=#@@@@@@@%: %@%@@@@@@@@+@@@@#++++=%@%@%%-%@+       +@+               
                .:-::    **********+  -**      :*=:   --         :.               
'@
    write-host $byline -ForegroundColor Magenta
    write-host $logo
    write-host -ForegroundColor Magenta "                 Microsoft Incident Response Critical Action Team"
}
function Write-Clawv3Logo {
    $byline = @'
        __  __ ___ ___  ___   _ _____   ___                    _      
        |  \/  |_ _| _ \/ __| /_|_   _| | _ \_ _ ___ ______ _ _| |_ ___
        | |\/| || ||   | (__ / _ \| |   |  _| '_/ -_(_-/ -_| ' |  _(_-<
        |_|  |_|___|_|_\\___/_/ \_|_|   |_| |_| \___/__\___|_||_\__/__/
'@

    write-host $byline -ForegroundColor Magenta
    #write-host $logo
    write-host "                                     .. "
    write-host "                           -*##*=   .@@=                                        " -ForegroundColor green
    write-host "                         -@@%++*@#. .@@=            -%@#.                       " -ForegroundColor green
    write-host "                         @@*        .@@=          -%@@@@.                       " -ForegroundColor green
    write-host "                        .@@+     .. .@@+        :#@%--@@     =@+                " -ForegroundColor green
    write-host "              :          +@@=   .%@% @@%####* .#@@@@%%@@    #@%-                " -ForegroundColor green
    write-host "            =@@+          =%@@%%@@*. ++++++==.+#=  :-%@%  -@@# .-+*:            " -ForegroundColor green
    write-host "          -%@%- -           .-==-                    *@# *@@%*%@@@@%            " -ForegroundColor green
    write-host "         =@@@::%@%. ::         " -ForegroundColor green -nonewline
    write-host ".:=*####%@-" -nonewline
    write-host "           .-=%@@@%*=-%@#  :=.        " -ForegroundColor green
#18
    write-host "          .+@@@@* .*@@-     " -ForegroundColor green -nonewline
    write-host "-*#*=:     #@=      *#*=.   " -nonewline
    write-host ":+-.  :@@#=*@@@*        " -ForegroundColor green
#19
    write-host "             =%@@*@@*.   " -ForegroundColor green -nonewline
    write-host "-##=.         #@=         -*#-      " -nonewline
    write-host "-@@@@@%+:          " -ForegroundColor green
#20
    write-host "     .=:.      -#@%:   " -ForegroundColor green -nonewline
    write-host "=%*.            #@=           .+*    " -nonewline
    write-host ".%@@#=.             " -ForegroundColor green
#21
    write-host "     *@@@@%#*=-: .   " -ForegroundColor green -nonewline
    write-host ":%*. -@*-         #@=                    " -nonewline
    write-host ":                 " -ForegroundColor green
#22
    write-host "        .%@%*%@@*" -ForegroundColor green -nonewline
    write-host "   =%:   %@@@@*.      +#-                              .@@     "
#23
    write-host "    -:. -@@-" -ForegroundColor green -nonewline
    write-host "       *%    :@@%=      .######-                             #@=    "
#24
    write-host "   :@@@@@@@=-:" -ForegroundColor green -nonewline
    write-host "    +%     #@=       %@@@@@@@@@+                           -@%    "
#25
    write-host "     .:-+*#%@@@." -ForegroundColor green -nonewline
    write-host " :@.    :%.        -%%#######:         .=+                @@.   "
#26
    write-host "             .:" -ForegroundColor green -nonewline
    write-host "  #*     +         -%:       +%.        -@@.               %@:   "    
#27
    write-host "       :==" -ForegroundColor green -nonewline
    write-host "       @:     .        -#.         -%.         #                %@-   "
#28
    write-host "   =@@..:-=+**#." -ForegroundColor green -nonewline
    write-host ":@             *%#:           -##-                        @@:   "
#29
    write-host "   .@@@@@@%#**+:" -ForegroundColor green -nonewline
    write-host ":@.            +%#:      .-   =%%=                        @@    "
#30
    write-host "    #@@." -ForegroundColor green -nonewline
    write-host "         -              -@ :      :   .%                         -@#    "
#31
    write-host "    =@%." -ForegroundColor green -nonewline
    write-host "                         +-@#=*###+:  -=                         %@=    "
    write-host "                             :%#- :@@@@@@@@@%==-           .%=          -@%     "
    write-host "                              -@@@*+@@@@@@@@@@@-          -%-           @@:     "
    write-host "                               :-+@@@@@@@@@@@#=+%%.     .##.           %@=      "
    write-host "                                  .@@@@@@@@@@@@@#.    .*%-            #@+       "
    write-host "        *@=                        @@@@@@@@@%: .    -*#-            .%@+        "
    write-host "         #@*                     :%@@@@@@@@@.      -+.             -@@-         "
    write-host "          =@@=                   =@@@@#@@@@@+                    .#@#.    "
    write-host ""
    write-host -ForegroundColor Magenta "                 Microsoft Incident Response Critical Action Team"
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
function Write-Logo {
    $logo = @'
    oclccccccccccccccccccccccccccccccccccxXWWNxoooooooolc:;;,,:oooooooooooooooooooox
    oclcccccccccccccccccccccccccccccccc:cxXWWXkool:;,"....    ;oooooooooooooooooooox
    oclccccccccccccccccccccccccclc:;,..":xXNKkc"..  ..",::.  "loooooooooooooooooooox
    oclccccccccccccccccccccccc:;"....  .",;"..  ..,:loooo;.  ..."",;:cloooooooooooox
    oclcccccccccccccclccccc:,..   ..      .,;:::looooooo:.     ........",:coooooooox
    occcccccccccccccccccc;".       ...",,lONWNkooooooooc"",;:cclllllcc::;;;:loooooox
    oclccccccccccccccc;".     ..",;:ccclcxXWWNkoooooooolloooooooooooooooooooooooooox
    oclccccccccclcc:,.    ..,:ccccccclcccxXWWNkoooooooooooooooooooooooooooooooooooox
    oclcccccccccc:".    .;ccccccccccllcccxXWWNkoooooooooooooooooooooooooooooooooooox
    oclcccccccl:".   .":clcccccccccccccc:dXWWXkoooooooooooooooooooooooooooooooooooox
    ocllcccccc;.   .":ccccccccccclc;,"...:KWWXkoooooooooooooooooooooooooooooooooooox
    ocllccccc"    .:cccccccccccc;"".    .oXWWNkoooolc:;;;;;;:cloooooooooooooooooooox
    occccclc"   .,ccccccccccc:,"","    .:xXWWXo;,"..          ..",:looooooooooooooox
    oclclcc"   .,ccccccccc:;".."".   .,ccodl:;...",,,,,""..        ..;coooooooooooox
    xodddd:.   "oooolc;,"..        .;lo:;:cdkkxdxxxxxxxxxxd;         .;oxxxxxxxxxxxk
    NNNNNNO"   .",,,;;;;:cloooo;.,oKNNKk0NWWWWNNNNNNNNNNN0l.     .;okKNNNNNNNNNNNNNN
    0OOOOOOl.   .,:odkOOOOOOOOOxokOOOOO0O0KXXXXXXXXXXXXKx"    .;oOKXXXXXXXXXXXXXXXXX
    occcccl;. .:clcccccccccccclcccccc:,"......"ckOOOOOx;    "lxOOOOOOOOOOOOOOOOOOOO0
    occccll, .:lcllclcc::clclcclcc:,.."",cxdo;  :kOOOx,   .lkOOOOOOOOOOOOOOOOOOOOOO0
    oclcllc" .,,,,,"....;cllcclc;.. "cllcxXWWXl..oOOk;   ;xOOOOOOOOOOOOOOOOOOOOOOOO0
    oclcclc"         .,clccccc,."...;cclcxXWWXl. ;kOl.  ;kOOOOOOOOOOOOOOOOOOOOOOOOO0
    occclcl,  .    .,:ccccc:,. .;c:;;";llxXWWXx, "xk,  .dOOOOOOOOOOOOOOOOOOOOOOOOOO0
    occcccl:..,;.  .;:cc:;"...  ,lccc".:lxXWWN0o..dx"  ;kOOOOOOOOOOOOOOOOOOOOOOOOOO0
    oclccccc:...     ....  .;c;,:lccl;.,lxXWWXo. .dx.  cOOOOOOOOOOOOOOOOOOOOOOOOOOO0
    ocllcccclc,.  .,:::;.   .cllccclc.."cxXWWNOc."xk,  cOOOOOOOOOOOOOOOOOOOOOOOOOOO0
    ocllccclccc:,..,ccccc;"..;lc:cllc.."";xKXX0d.,kOc. ,xOOOOOOOOOOOOOOOOOOOOOOOOOO0
    oclccccccccclc;;clclcclc;:l:..;;"..:;. ..""..lOOx,  :kOOOOOOOOOOOOOOOOOOOOOOOOO0
    oclcccccclccccllcclccccclllc,     ."".......;kOOOd" .:kOOOOOOOOOOOOOOOOOOOOOOOO0
    oclcccccccccccccccccccccclllc,.    .,lOKK0kdkOOOOOd"  ,dOOOOOOOOOOOOOOOOOOOOOOO0
    oclcccccccccccccccccccccllllclc;..  ."coddoooodoolc.   .cxOOOOOOOOOOOOOOOOOOOOO0
    oclcccccccccccccccccccccccllcllllc;,"";,""..............,lkOOOOOOOOOOOOOOOOOOOO0
'@
    write-host $logo
    write-host -ForegroundColor Magenta "    Microsoft Incident Response Critical Action Team"
    #Test comment :)
}

# SIG # Begin signature block
# MIIoKgYJKoZIhvcNAQcCoIIoGzCCKBcCAQExDzANBglghkgBZQMEAgEFADB5Bgor
# BgEEAYI3AgEEoGswaTA0BgorBgEEAYI3AgEeMCYCAwEAAAQQH8w7YFlLCE63JNLG
# KX7zUQIBAAIBAAIBAAIBAAIBADAxMA0GCWCGSAFlAwQCAQUABCANhVhN8W2ejDuF
# kwXvoE8zkHiQ8sVZ5Ib7HHkLS5BR46CCDXYwggX0MIID3KADAgECAhMzAAAEhV6Z
# 7A5ZL83XAAAAAASFMA0GCSqGSIb3DQEBCwUAMH4xCzAJBgNVBAYTAlVTMRMwEQYD
# VQQIEwpXYXNoaW5ndG9uMRAwDgYDVQQHEwdSZWRtb25kMR4wHAYDVQQKExVNaWNy
# b3NvZnQgQ29ycG9yYXRpb24xKDAmBgNVBAMTH01pY3Jvc29mdCBDb2RlIFNpZ25p
# bmcgUENBIDIwMTEwHhcNMjUwNjE5MTgyMTM3WhcNMjYwNjE3MTgyMTM3WjB0MQsw
# CQYDVQQGEwJVUzETMBEGA1UECBMKV2FzaGluZ3RvbjEQMA4GA1UEBxMHUmVkbW9u
# ZDEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMR4wHAYDVQQDExVNaWNy
# b3NvZnQgQ29ycG9yYXRpb24wggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIB
# AQDASkh1cpvuUqfbqxele7LCSHEamVNBfFE4uY1FkGsAdUF/vnjpE1dnAD9vMOqy
# 5ZO49ILhP4jiP/P2Pn9ao+5TDtKmcQ+pZdzbG7t43yRXJC3nXvTGQroodPi9USQi
# 9rI+0gwuXRKBII7L+k3kMkKLmFrsWUjzgXVCLYa6ZH7BCALAcJWZTwWPoiT4HpqQ
# hJcYLB7pfetAVCeBEVZD8itKQ6QA5/LQR+9X6dlSj4Vxta4JnpxvgSrkjXCz+tlJ
# 67ABZ551lw23RWU1uyfgCfEFhBfiyPR2WSjskPl9ap6qrf8fNQ1sGYun2p4JdXxe
# UAKf1hVa/3TQXjvPTiRXCnJPAgMBAAGjggFzMIIBbzAfBgNVHSUEGDAWBgorBgEE
# AYI3TAgBBggrBgEFBQcDAzAdBgNVHQ4EFgQUuCZyGiCuLYE0aU7j5TFqY05kko0w
# RQYDVR0RBD4wPKQ6MDgxHjAcBgNVBAsTFU1pY3Jvc29mdCBDb3Jwb3JhdGlvbjEW
# MBQGA1UEBRMNMjMwMDEyKzUwNTM1OTAfBgNVHSMEGDAWgBRIbmTlUAXTgqoXNzci
# tW2oynUClTBUBgNVHR8ETTBLMEmgR6BFhkNodHRwOi8vd3d3Lm1pY3Jvc29mdC5j
# b20vcGtpb3BzL2NybC9NaWNDb2RTaWdQQ0EyMDExXzIwMTEtMDctMDguY3JsMGEG
# CCsGAQUFBwEBBFUwUzBRBggrBgEFBQcwAoZFaHR0cDovL3d3dy5taWNyb3NvZnQu
# Y29tL3BraW9wcy9jZXJ0cy9NaWNDb2RTaWdQQ0EyMDExXzIwMTEtMDctMDguY3J0
# MAwGA1UdEwEB/wQCMAAwDQYJKoZIhvcNAQELBQADggIBACjmqAp2Ci4sTHZci+qk
# tEAKsFk5HNVGKyWR2rFGXsd7cggZ04H5U4SV0fAL6fOE9dLvt4I7HBHLhpGdE5Uj
# Ly4NxLTG2bDAkeAVmxmd2uKWVGKym1aarDxXfv3GCN4mRX+Pn4c+py3S/6Kkt5eS
# DAIIsrzKw3Kh2SW1hCwXX/k1v4b+NH1Fjl+i/xPJspXCFuZB4aC5FLT5fgbRKqns
# WeAdn8DsrYQhT3QXLt6Nv3/dMzv7G/Cdpbdcoul8FYl+t3dmXM+SIClC3l2ae0wO
# lNrQ42yQEycuPU5OoqLT85jsZ7+4CaScfFINlO7l7Y7r/xauqHbSPQ1r3oIC+e71
# 5s2G3ClZa3y99aYx2lnXYe1srcrIx8NAXTViiypXVn9ZGmEkfNcfDiqGQwkml5z9
# nm3pWiBZ69adaBBbAFEjyJG4y0a76bel/4sDCVvaZzLM3TFbxVO9BQrjZRtbJZbk
# C3XArpLqZSfx53SuYdddxPX8pvcqFuEu8wcUeD05t9xNbJ4TtdAECJlEi0vvBxlm
# M5tzFXy2qZeqPMXHSQYqPgZ9jvScZ6NwznFD0+33kbzyhOSz/WuGbAu4cHZG8gKn
# lQVT4uA2Diex9DMs2WHiokNknYlLoUeWXW1QrJLpqO82TLyKTbBM/oZHAdIc0kzo
# STro9b3+vjn2809D0+SOOCVZMIIHejCCBWKgAwIBAgIKYQ6Q0gAAAAAAAzANBgkq
# hkiG9w0BAQsFADCBiDELMAkGA1UEBhMCVVMxEzARBgNVBAgTCldhc2hpbmd0b24x
# EDAOBgNVBAcTB1JlZG1vbmQxHjAcBgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlv
# bjEyMDAGA1UEAxMpTWljcm9zb2Z0IFJvb3QgQ2VydGlmaWNhdGUgQXV0aG9yaXR5
# IDIwMTEwHhcNMTEwNzA4MjA1OTA5WhcNMjYwNzA4MjEwOTA5WjB+MQswCQYDVQQG
# EwJVUzETMBEGA1UECBMKV2FzaGluZ3RvbjEQMA4GA1UEBxMHUmVkbW9uZDEeMBwG
# A1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMSgwJgYDVQQDEx9NaWNyb3NvZnQg
# Q29kZSBTaWduaW5nIFBDQSAyMDExMIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIIC
# CgKCAgEAq/D6chAcLq3YbqqCEE00uvK2WCGfQhsqa+laUKq4BjgaBEm6f8MMHt03
# a8YS2AvwOMKZBrDIOdUBFDFC04kNeWSHfpRgJGyvnkmc6Whe0t+bU7IKLMOv2akr
# rnoJr9eWWcpgGgXpZnboMlImEi/nqwhQz7NEt13YxC4Ddato88tt8zpcoRb0Rrrg
# OGSsbmQ1eKagYw8t00CT+OPeBw3VXHmlSSnnDb6gE3e+lD3v++MrWhAfTVYoonpy
# 4BI6t0le2O3tQ5GD2Xuye4Yb2T6xjF3oiU+EGvKhL1nkkDstrjNYxbc+/jLTswM9
# sbKvkjh+0p2ALPVOVpEhNSXDOW5kf1O6nA+tGSOEy/S6A4aN91/w0FK/jJSHvMAh
# dCVfGCi2zCcoOCWYOUo2z3yxkq4cI6epZuxhH2rhKEmdX4jiJV3TIUs+UsS1Vz8k
# A/DRelsv1SPjcF0PUUZ3s/gA4bysAoJf28AVs70b1FVL5zmhD+kjSbwYuER8ReTB
# w3J64HLnJN+/RpnF78IcV9uDjexNSTCnq47f7Fufr/zdsGbiwZeBe+3W7UvnSSmn
# Eyimp31ngOaKYnhfsi+E11ecXL93KCjx7W3DKI8sj0A3T8HhhUSJxAlMxdSlQy90
# lfdu+HggWCwTXWCVmj5PM4TasIgX3p5O9JawvEagbJjS4NaIjAsCAwEAAaOCAe0w
# ggHpMBAGCSsGAQQBgjcVAQQDAgEAMB0GA1UdDgQWBBRIbmTlUAXTgqoXNzcitW2o
# ynUClTAZBgkrBgEEAYI3FAIEDB4KAFMAdQBiAEMAQTALBgNVHQ8EBAMCAYYwDwYD
# VR0TAQH/BAUwAwEB/zAfBgNVHSMEGDAWgBRyLToCMZBDuRQFTuHqp8cx0SOJNDBa
# BgNVHR8EUzBRME+gTaBLhklodHRwOi8vY3JsLm1pY3Jvc29mdC5jb20vcGtpL2Ny
# bC9wcm9kdWN0cy9NaWNSb29DZXJBdXQyMDExXzIwMTFfMDNfMjIuY3JsMF4GCCsG
# AQUFBwEBBFIwUDBOBggrBgEFBQcwAoZCaHR0cDovL3d3dy5taWNyb3NvZnQuY29t
# L3BraS9jZXJ0cy9NaWNSb29DZXJBdXQyMDExXzIwMTFfMDNfMjIuY3J0MIGfBgNV
# HSAEgZcwgZQwgZEGCSsGAQQBgjcuAzCBgzA/BggrBgEFBQcCARYzaHR0cDovL3d3
# dy5taWNyb3NvZnQuY29tL3BraW9wcy9kb2NzL3ByaW1hcnljcHMuaHRtMEAGCCsG
# AQUFBwICMDQeMiAdAEwAZQBnAGEAbABfAHAAbwBsAGkAYwB5AF8AcwB0AGEAdABl
# AG0AZQBuAHQALiAdMA0GCSqGSIb3DQEBCwUAA4ICAQBn8oalmOBUeRou09h0ZyKb
# C5YR4WOSmUKWfdJ5DJDBZV8uLD74w3LRbYP+vj/oCso7v0epo/Np22O/IjWll11l
# hJB9i0ZQVdgMknzSGksc8zxCi1LQsP1r4z4HLimb5j0bpdS1HXeUOeLpZMlEPXh6
# I/MTfaaQdION9MsmAkYqwooQu6SpBQyb7Wj6aC6VoCo/KmtYSWMfCWluWpiW5IP0
# wI/zRive/DvQvTXvbiWu5a8n7dDd8w6vmSiXmE0OPQvyCInWH8MyGOLwxS3OW560
# STkKxgrCxq2u5bLZ2xWIUUVYODJxJxp/sfQn+N4sOiBpmLJZiWhub6e3dMNABQam
# ASooPoI/E01mC8CzTfXhj38cbxV9Rad25UAqZaPDXVJihsMdYzaXht/a8/jyFqGa
# J+HNpZfQ7l1jQeNbB5yHPgZ3BtEGsXUfFL5hYbXw3MYbBL7fQccOKO7eZS/sl/ah
# XJbYANahRr1Z85elCUtIEJmAH9AAKcWxm6U/RXceNcbSoqKfenoi+kiVH6v7RyOA
# 9Z74v2u3S5fi63V4GuzqN5l5GEv/1rMjaHXmr/r8i+sLgOppO6/8MO0ETI7f33Vt
# Y5E90Z1WTk+/gFcioXgRMiF670EKsT/7qMykXcGhiJtXcVZOSEXAQsmbdlsKgEhr
# /Xmfwb1tbWrJUnMTDXpQzTGCGgowghoGAgEBMIGVMH4xCzAJBgNVBAYTAlVTMRMw
# EQYDVQQIEwpXYXNoaW5ndG9uMRAwDgYDVQQHEwdSZWRtb25kMR4wHAYDVQQKExVN
# aWNyb3NvZnQgQ29ycG9yYXRpb24xKDAmBgNVBAMTH01pY3Jvc29mdCBDb2RlIFNp
# Z25pbmcgUENBIDIwMTECEzMAAASFXpnsDlkvzdcAAAAABIUwDQYJYIZIAWUDBAIB
# BQCgga4wGQYJKoZIhvcNAQkDMQwGCisGAQQBgjcCAQQwHAYKKwYBBAGCNwIBCzEO
# MAwGCisGAQQBgjcCARUwLwYJKoZIhvcNAQkEMSIEIDTAsSHU5xUnF+W4RbKGzswZ
# y8f3Ch8qw221Mb1jVdBrMEIGCisGAQQBgjcCAQwxNDAyoBSAEgBNAGkAYwByAG8A
# cwBvAGYAdKEagBhodHRwOi8vd3d3Lm1pY3Jvc29mdC5jb20wDQYJKoZIhvcNAQEB
# BQAEggEAUPVcxiN7gdawyUpqlpHDMsDkDaEJ8eIVd55yLFj255OdIYca59nykPKE
# ezTxJTwa3vTXrO2wJ/QkQUZ54wQd2Zum80ZGyFlqjwKnyOCT2lnAHArz9PN1kHyz
# CmjZwdsI2/nLq2FHr9jz3fqkAslIfJuzM3/VMr5Umwwx22qIoZiBhXPgCMHvw6cp
# IMq+aGl12ZWm1utLDzeWevWXERgMIAiXLyG6I3SEy7O+AmyIZjMIMey/sI18ncho
# Aa6gmC6PM5/m09YwrKlPP/Mfzq4BuG2AoZ50gxzohEWWHFPdw+45ugAVANcCftaK
# zDRbn64iKaHREXfHxHDhwDJ06zc8FqGCF5QwgheQBgorBgEEAYI3AwMBMYIXgDCC
# F3wGCSqGSIb3DQEHAqCCF20wghdpAgEDMQ8wDQYJYIZIAWUDBAIBBQAwggFSBgsq
# hkiG9w0BCRABBKCCAUEEggE9MIIBOQIBAQYKKwYBBAGEWQoDATAxMA0GCWCGSAFl
# AwQCAQUABCD4KMCY9ANa1dHckH3VxgdVT9XfmmyR7yJmfd/SgpkxuAIGaKOlAEIm
# GBMyMDI1MDgyNzE4MDk0OS43NzNaMASAAgH0oIHRpIHOMIHLMQswCQYDVQQGEwJV
# UzETMBEGA1UECBMKV2FzaGluZ3RvbjEQMA4GA1UEBxMHUmVkbW9uZDEeMBwGA1UE
# ChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMSUwIwYDVQQLExxNaWNyb3NvZnQgQW1l
# cmljYSBPcGVyYXRpb25zMScwJQYDVQQLEx5uU2hpZWxkIFRTUyBFU046RTAwMi0w
# NUUwLUQ5NDcxJTAjBgNVBAMTHE1pY3Jvc29mdCBUaW1lLVN0YW1wIFNlcnZpY2Wg
# ghHqMIIHIDCCBQigAwIBAgITMwAAAgsRnVYpkvm/hQABAAACCzANBgkqhkiG9w0B
# AQsFADB8MQswCQYDVQQGEwJVUzETMBEGA1UECBMKV2FzaGluZ3RvbjEQMA4GA1UE
# BxMHUmVkbW9uZDEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMSYwJAYD
# VQQDEx1NaWNyb3NvZnQgVGltZS1TdGFtcCBQQ0EgMjAxMDAeFw0yNTAxMzAxOTQy
# NThaFw0yNjA0MjIxOTQyNThaMIHLMQswCQYDVQQGEwJVUzETMBEGA1UECBMKV2Fz
# aGluZ3RvbjEQMA4GA1UEBxMHUmVkbW9uZDEeMBwGA1UEChMVTWljcm9zb2Z0IENv
# cnBvcmF0aW9uMSUwIwYDVQQLExxNaWNyb3NvZnQgQW1lcmljYSBPcGVyYXRpb25z
# MScwJQYDVQQLEx5uU2hpZWxkIFRTUyBFU046RTAwMi0wNUUwLUQ5NDcxJTAjBgNV
# BAMTHE1pY3Jvc29mdCBUaW1lLVN0YW1wIFNlcnZpY2UwggIiMA0GCSqGSIb3DQEB
# AQUAA4ICDwAwggIKAoICAQCqrPitRjAXqFh2IHzQYD3uykDPyJF+79e5CkY4aYsb
# 93QVun4fZ3Ju/0WHrtAF3JswSiAVl7p1H2zFKrvyhaVuRYcSc7YuyP0GHEVq7YVS
# 5uF3YLlLeoyGOPKSXGs6agW60CqVBhPQ+2n49e6YD9wGv6Y0HmBKmnQqY/AKJijg
# UiRulb1ovNEcTZmTNRu1mY+0JjiEus+eF66VNoBv1a2MW0JPYbFBhPzFHlddFXcj
# f2qIkb5BYWsFL7QlBjXApf2HmNrPzG36g1ybo/KnRjSgIRpHeYXxBIaCEGtR1Emp
# J90OSFHxUu7eIjVfenqnVtag0yAQY7zEWSXMN6+CHjv3SBNtm5ZIRyyCsUZG8454
# K+865bw7FwuH8vk5Q+07K5lFY02eBDw3UKzWjWvqTp2pK8MTa4kozvlKgrSGp5sh
# 57GnkjlvNvt78NXbZTVIrwS7xcIGjbvS/2r5lRDT+Q3P2tT+g6KDPdLntlcbFdHu
# uzyJyx0WfCr8zHv8wGCB3qPObRXK4opAInSQ4j5iS28KATJGwQabRueZvhvd9Od0
# wcFYOb4orUv1dD5XwFyKlGDPMcTPOQr0gxmEQVrLiJEoLyyW8EV/aDFUXToxyhfz
# WZ6Dc0l9eeth1Et2NQ3A/qBR5x33pjKdHJVJ5xpp2AI3ZzNYLDCqO1lthz1GaSz+
# PQIDAQABo4IBSTCCAUUwHQYDVR0OBBYEFGZcLIjfr+l6WeMuhE9gsxe98j/+MB8G
# A1UdIwQYMBaAFJ+nFV0AXmJdg/Tl0mWnG1M1GelyMF8GA1UdHwRYMFYwVKBSoFCG
# Tmh0dHA6Ly93d3cubWljcm9zb2Z0LmNvbS9wa2lvcHMvY3JsL01pY3Jvc29mdCUy
# MFRpbWUtU3RhbXAlMjBQQ0ElMjAyMDEwKDEpLmNybDBsBggrBgEFBQcBAQRgMF4w
# XAYIKwYBBQUHMAKGUGh0dHA6Ly93d3cubWljcm9zb2Z0LmNvbS9wa2lvcHMvY2Vy
# dHMvTWljcm9zb2Z0JTIwVGltZS1TdGFtcCUyMFBDQSUyMDIwMTAoMSkuY3J0MAwG
# A1UdEwEB/wQCMAAwFgYDVR0lAQH/BAwwCgYIKwYBBQUHAwgwDgYDVR0PAQH/BAQD
# AgeAMA0GCSqGSIb3DQEBCwUAA4ICAQCaKPVn6GLcnkbPEdM0R9q4Zm0+7JfG05+p
# mqP6nA4SwT26k9HlJQjqw/+WkiQLD4owJxooIr9MDZbiZX6ypPhF+g1P5u8BOEXP
# YYkOWpzFGLRLtlZHvfxpqAIa7mjLGHDzKr/102AXaD4mGydEwaLGhUn9DBGdMm5d
# hiisWAqb/LN4lm4OuX4YLqKcW/0yScHKgprGgLY+6pqv0zPU74j7eCr+PDTNYM8t
# FJ/btUnBNLyOE4WZwBIq4tnvXjd2cCOtgUnoQjFU1ZY7ZWdny3BJbf3hBrb3NB2I
# U4nu622tVrb1fNkwdvT501WRUBMd9oFf4xifj2j2Clbv1XGljXmd6yJjvt+bBuvJ
# LUuc9m+vMKOWyRwUdvOl/E5a8zV3MrjCnY6fIrLQNzBOZ6klICPCi+2GqbViM0CI
# 6CbZypei5Rr9hJbH8rZEzjaYWLnr/XPsU0wr2Tn6L9dJx2q/LAoK+oviAInj0aP4
# iRrMyUSO6KL2KwY6zJc6SDxbHkwYHdQRrPNP3SutMg6LgBSvtmfqwgaXIHkCoiUF
# EAz9cGIqvgjGpGppKTcTuoo3EEgp/zRd0wxW0QqmV3ygYGicen30KAWHrKFC8Sbw
# c6qC4podVZYJZmirHBP/uo7sQne5H0xtdvDmXDUfy5gNjLljQIUsJhQSyyXbSjSb
# 2a5jhOUfxzCCB3EwggVZoAMCAQICEzMAAAAVxedrngKbSZkAAAAAABUwDQYJKoZI
# hvcNAQELBQAwgYgxCzAJBgNVBAYTAlVTMRMwEQYDVQQIEwpXYXNoaW5ndG9uMRAw
# DgYDVQQHEwdSZWRtb25kMR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9yYXRpb24x
# MjAwBgNVBAMTKU1pY3Jvc29mdCBSb290IENlcnRpZmljYXRlIEF1dGhvcml0eSAy
# MDEwMB4XDTIxMDkzMDE4MjIyNVoXDTMwMDkzMDE4MzIyNVowfDELMAkGA1UEBhMC
# VVMxEzARBgNVBAgTCldhc2hpbmd0b24xEDAOBgNVBAcTB1JlZG1vbmQxHjAcBgNV
# BAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlvbjEmMCQGA1UEAxMdTWljcm9zb2Z0IFRp
# bWUtU3RhbXAgUENBIDIwMTAwggIiMA0GCSqGSIb3DQEBAQUAA4ICDwAwggIKAoIC
# AQDk4aZM57RyIQt5osvXJHm9DtWC0/3unAcH0qlsTnXIyjVX9gF/bErg4r25Phdg
# M/9cT8dm95VTcVrifkpa/rg2Z4VGIwy1jRPPdzLAEBjoYH1qUoNEt6aORmsHFPPF
# dvWGUNzBRMhxXFExN6AKOG6N7dcP2CZTfDlhAnrEqv1yaa8dq6z2Nr41JmTamDu6
# GnszrYBbfowQHJ1S/rboYiXcag/PXfT+jlPP1uyFVk3v3byNpOORj7I5LFGc6XBp
# Dco2LXCOMcg1KL3jtIckw+DJj361VI/c+gVVmG1oO5pGve2krnopN6zL64NF50Zu
# yjLVwIYwXE8s4mKyzbnijYjklqwBSru+cakXW2dg3viSkR4dPf0gz3N9QZpGdc3E
# XzTdEonW/aUgfX782Z5F37ZyL9t9X4C626p+Nuw2TPYrbqgSUei/BQOj0XOmTTd0
# lBw0gg/wEPK3Rxjtp+iZfD9M269ewvPV2HM9Q07BMzlMjgK8QmguEOqEUUbi0b1q
# GFphAXPKZ6Je1yh2AuIzGHLXpyDwwvoSCtdjbwzJNmSLW6CmgyFdXzB0kZSU2LlQ
# +QuJYfM2BjUYhEfb3BvR/bLUHMVr9lxSUV0S2yW6r1AFemzFER1y7435UsSFF5PA
# PBXbGjfHCBUYP3irRbb1Hode2o+eFnJpxq57t7c+auIurQIDAQABo4IB3TCCAdkw
# EgYJKwYBBAGCNxUBBAUCAwEAATAjBgkrBgEEAYI3FQIEFgQUKqdS/mTEmr6CkTxG
# NSnPEP8vBO4wHQYDVR0OBBYEFJ+nFV0AXmJdg/Tl0mWnG1M1GelyMFwGA1UdIARV
# MFMwUQYMKwYBBAGCN0yDfQEBMEEwPwYIKwYBBQUHAgEWM2h0dHA6Ly93d3cubWlj
# cm9zb2Z0LmNvbS9wa2lvcHMvRG9jcy9SZXBvc2l0b3J5Lmh0bTATBgNVHSUEDDAK
# BggrBgEFBQcDCDAZBgkrBgEEAYI3FAIEDB4KAFMAdQBiAEMAQTALBgNVHQ8EBAMC
# AYYwDwYDVR0TAQH/BAUwAwEB/zAfBgNVHSMEGDAWgBTV9lbLj+iiXGJo0T2UkFvX
# zpoYxDBWBgNVHR8ETzBNMEugSaBHhkVodHRwOi8vY3JsLm1pY3Jvc29mdC5jb20v
# cGtpL2NybC9wcm9kdWN0cy9NaWNSb29DZXJBdXRfMjAxMC0wNi0yMy5jcmwwWgYI
# KwYBBQUHAQEETjBMMEoGCCsGAQUFBzAChj5odHRwOi8vd3d3Lm1pY3Jvc29mdC5j
# b20vcGtpL2NlcnRzL01pY1Jvb0NlckF1dF8yMDEwLTA2LTIzLmNydDANBgkqhkiG
# 9w0BAQsFAAOCAgEAnVV9/Cqt4SwfZwExJFvhnnJL/Klv6lwUtj5OR2R4sQaTlz0x
# M7U518JxNj/aZGx80HU5bbsPMeTCj/ts0aGUGCLu6WZnOlNN3Zi6th542DYunKmC
# VgADsAW+iehp4LoJ7nvfam++Kctu2D9IdQHZGN5tggz1bSNU5HhTdSRXud2f8449
# xvNo32X2pFaq95W2KFUn0CS9QKC/GbYSEhFdPSfgQJY4rPf5KYnDvBewVIVCs/wM
# nosZiefwC2qBwoEZQhlSdYo2wh3DYXMuLGt7bj8sCXgU6ZGyqVvfSaN0DLzskYDS
# PeZKPmY7T7uG+jIa2Zb0j/aRAfbOxnT99kxybxCrdTDFNLB62FD+CljdQDzHVG2d
# Y3RILLFORy3BFARxv2T5JL5zbcqOCb2zAVdJVGTZc9d/HltEAY5aGZFrDZ+kKNxn
# GSgkujhLmm77IVRrakURR6nxt67I6IleT53S0Ex2tVdUCbFpAUR+fKFhbHP+Crvs
# QWY9af3LwUFJfn6Tvsv4O+S3Fb+0zj6lMVGEvL8CwYKiexcdFYmNcP7ntdAoGokL
# jzbaukz5m/8K6TT4JDVnK+ANuOaMmdbhIurwJ0I9JZTmdHRbatGePu1+oDEzfbzL
# 6Xu/OHBE0ZDxyKs6ijoIYn/ZcGNTTY3ugm2lBRDBcQZqELQdVTNYs6FwZvKhggNN
# MIICNQIBATCB+aGB0aSBzjCByzELMAkGA1UEBhMCVVMxEzARBgNVBAgTCldhc2hp
# bmd0b24xEDAOBgNVBAcTB1JlZG1vbmQxHjAcBgNVBAoTFU1pY3Jvc29mdCBDb3Jw
# b3JhdGlvbjElMCMGA1UECxMcTWljcm9zb2Z0IEFtZXJpY2EgT3BlcmF0aW9uczEn
# MCUGA1UECxMeblNoaWVsZCBUU1MgRVNOOkUwMDItMDVFMC1EOTQ3MSUwIwYDVQQD
# ExxNaWNyb3NvZnQgVGltZS1TdGFtcCBTZXJ2aWNloiMKAQEwBwYFKw4DAhoDFQCo
# QndUJN3Ppq2xh8RhtsR35NCZwaCBgzCBgKR+MHwxCzAJBgNVBAYTAlVTMRMwEQYD
# VQQIEwpXYXNoaW5ndG9uMRAwDgYDVQQHEwdSZWRtb25kMR4wHAYDVQQKExVNaWNy
# b3NvZnQgQ29ycG9yYXRpb24xJjAkBgNVBAMTHU1pY3Jvc29mdCBUaW1lLVN0YW1w
# IFBDQSAyMDEwMA0GCSqGSIb3DQEBCwUAAgUA7FlXXDAiGA8yMDI1MDgyNzEwMDcy
# NFoYDzIwMjUwODI4MTAwNzI0WjB0MDoGCisGAQQBhFkKBAExLDAqMAoCBQDsWVdc
# AgEAMAcCAQACAivCMAcCAQACAhKqMAoCBQDsWqjcAgEAMDYGCisGAQQBhFkKBAIx
# KDAmMAwGCisGAQQBhFkKAwKgCjAIAgEAAgMHoSChCjAIAgEAAgMBhqAwDQYJKoZI
# hvcNAQELBQADggEBADWJTKduRcEx8KwabRUGiyfG/gkXUi77X/lTCKP8fQWZsRvm
# YBbiRK0QGN5+PdiaEQp7Ri8Hnbkarl92Kc+GB44u2cbw1Os0sI4vyUqHUu+cDNe/
# GehnePgYHQgAJ98eGwgZqSBpQjtXtnCFP0tQHDT2HcrL+fmutXb0b1Po9N3nU1kl
# z/l6xiNR5Hlqdgy/Dy1DBlj1QSuC3budg05H/8ytcFAEZeJL9f+/2clYHJe6JoEj
# nxKi6JMxgT6IHlzss6bSlaTTnDUhegrW5QQzW5/xmao4AlzjiJX1hbnNF+nJvWrr
# qRV9X0xu4hvUn09PK2QToMwUG+3V/by9hCTaAigxggQNMIIECQIBATCBkzB8MQsw
# CQYDVQQGEwJVUzETMBEGA1UECBMKV2FzaGluZ3RvbjEQMA4GA1UEBxMHUmVkbW9u
# ZDEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMSYwJAYDVQQDEx1NaWNy
# b3NvZnQgVGltZS1TdGFtcCBQQ0EgMjAxMAITMwAAAgsRnVYpkvm/hQABAAACCzAN
# BglghkgBZQMEAgEFAKCCAUowGgYJKoZIhvcNAQkDMQ0GCyqGSIb3DQEJEAEEMC8G
# CSqGSIb3DQEJBDEiBCC2g+ZoihD/0+bSA+XLkJvBLCibwe5o3zOmACEYaN8TkjCB
# +gYLKoZIhvcNAQkQAi8xgeowgecwgeQwgb0EIDTVdKu6N77bh0wdOyF+ogRN8vKJ
# cw5jnf2/EussYkozMIGYMIGApH4wfDELMAkGA1UEBhMCVVMxEzARBgNVBAgTCldh
# c2hpbmd0b24xEDAOBgNVBAcTB1JlZG1vbmQxHjAcBgNVBAoTFU1pY3Jvc29mdCBD
# b3Jwb3JhdGlvbjEmMCQGA1UEAxMdTWljcm9zb2Z0IFRpbWUtU3RhbXAgUENBIDIw
# MTACEzMAAAILEZ1WKZL5v4UAAQAAAgswIgQg/zugUp7Xl0JcudISDI+CEWHdOMo6
# 4S+zniIM4Yn5pYkwDQYJKoZIhvcNAQELBQAEggIAgIvYrLRpFbK782LkazEqrw1x
# d+EFory2cTb3LdAH7nP6FmWoY2Aj4TBgZ01P1untuvplc5jHXE+TALVgwPiZUnIX
# PP1EW14CTVUT4z1KOUADk/T56wK3lhxyLiEwYJX5XSLK4CRrYMu//mUn/228kVmD
# alvBjePALBRIB4K4lXLxl4u57A+R+DyEzzsAlKFS29vN6NsoCDI/rZdM3xA2sv0X
# IWo1kX4QYrcVWho6ON9qpfM580AdF5+3EiGYX8niRgSKwSD3sPl0BjEdoYgbKWse
# bEAiFTBJuTQOJkM3sjFCX1jlStZVPrBFg+fI5VDR/GCIHRO2JD2RLQxHq85ANXNH
# cpbId10GhzW1ak2W6wWMKkrMXwFxeTA5+N7wegXfFli+PDsmsFSYWQudbCif2dHN
# A8iY9b46utDZvynIu5Dis5TX3yrnnqZMl9INOgtyz5ZDp9hysJiho/PI+vxrXPHX
# 6bQTRyMmTB9Y4kbq5OzABo1THipNK7hZDtsqgK4ZEtZLA71aIsvNUrd2qPB2ID7/
# R3s5v+STXMA1iVgh6ngRfdXB2/IVa4fQNV63/LAp9raKhpDPGBS54PcaE762GapI
# 3yrzLshls3GJY6PFOca4ngZyJZGu2q3HMQBIDZyepym/LXX16NDGd2DZ8c++KSuP
# U0QKc9iUSrv7gyD5Fx8=
# SIG # End signature block
