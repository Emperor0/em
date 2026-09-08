Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
$script:D7MutexCreated=$false
$script:D7Mutex=New-Object System.Threading.Mutex($true,'Local\D7_BLACKCORE_SINGLE_INSTANCE',[ref]$script:D7MutexCreated)
if(-not $script:D7MutexCreated){[Windows.Forms.MessageBox]::Show('D7 BLACKCORE is already running. Use the existing window.','D7 BLACKCORE')|Out-Null;exit}

$Version='2.1.3'
$ManifestUrl='https://raw.githubusercontent.com/Emperor0/em/main/d7-gaming-engine/stable/latest.json'
$AppRoot=Join-Path $env:ProgramData 'D7 Gaming Engine'
$DataRoot=Join-Path $AppRoot 'BLACKCORE'
$ScanRoot=Join-Path $DataRoot 'Scans'
$StateRoot=Join-Path $DataRoot 'State'
$ToolRoot=Join-Path $DataRoot 'Tools'
$BackupFile=Join-Path $StateRoot 'deep-profile-backup.json'
$LearnFile=Join-Path $StateRoot 'learned-hardware.json'
$GamesFile=Join-Path $StateRoot 'learned-games.json'
$UpdateRoot=Join-Path $DataRoot 'Updates'
@($AppRoot,$DataRoot,$ScanRoot,$StateRoot,$ToolRoot,$UpdateRoot)|ForEach-Object{New-Item -ItemType Directory -Path $_ -Force|Out-Null}
$LogFile=Join-Path $DataRoot ("BLACKCORE_{0}.log" -f (Get-Date -Format 'yyyyMMdd_HHmmss'))

$script:LearnedGames=@();$script:GamePid=0;$script:GameName='';$script:SessionActive=$false;$script:PriorityBackup=@{};$script:UpdateBusy=$false;$script:LastScan=$null;$script:LastPlan=$null

function Log([string]$s){$l='[{0}] {1}'-f(Get-Date -Format 'HH:mm:ss'),$s;try{Add-Content $LogFile $l -Encoding UTF8}catch{};if ($script:LogBox){$script:LogBox.AppendText($l+[Environment]::NewLine);$script:LogBox.SelectionStart=$script:LogBox.TextLength;$script:LogBox.ScrollToCaret()}}
function RegGet([string]$p,[string]$n){try{$x=Get-ItemProperty -Path $p -Name $n -ErrorAction Stop;[pscustomobject]@{Exists=$true;Value=$x.$n}}catch{[pscustomobject]@{Exists=$false;Value=$null}}}
function RegSetD([string]$p,[string]$n,[int]$v){New-Item $p -Force|Out-Null;New-ItemProperty $p -Name $n -PropertyType DWord -Value $v -Force|Out-Null}
function RegSetS([string]$p,[string]$n,[string]$v){New-Item $p -Force|Out-Null;New-ItemProperty $p -Name $n -PropertyType String -Value $v -Force|Out-Null}
function ActivePower(){try{$s=& powercfg /GETACTIVESCHEME 2>&1|Out-String;if ($s-match'([0-9a-fA-F-]{36})'){return $matches[1]}}catch{};return $null}
function BackupReg($list,$p,$n,$t){$g=RegGet $p $n;[void]$list.Add([pscustomobject]@{Path=$p;Name=$n;Type=$t;Exists=$g.Exists;Value=$g.Value})}
function RestoreReg($e){try{if ($e.Exists){if ($e.Type-eq'DWord'){RegSetD $e.Path $e.Name ([int]$e.Value)}else{RegSetS $e.Path $e.Name ([string]$e.Value)}}else{Remove-ItemProperty -Path $e.Path -Name $e.Name -Force -ErrorAction SilentlyContinue}}catch{}}
function FileVersion($p){try{([Diagnostics.FileVersionInfo]::GetVersionInfo($p)).FileVersion}catch{return $null}}

function FindTool([string]$name,[string[]]$paths){foreach ($p in $paths){if (Test-Path $p){return [pscustomobject]@{Name=$name;Installed=$true;Path=$p;Version=(FileVersion $p)}}};return [pscustomobject]@{Name=$name;Installed=$false;Path=$null;Version=$null}}
function ToolInventory{
 $pf=$env:ProgramFiles;$pf86=${env:ProgramFiles(x86)};$la=$env:LOCALAPPDATA
 @(
  FindTool 'MSI Afterburner' @("$pf86\MSI Afterburner\MSIAfterburner.exe","$pf\MSI Afterburner\MSIAfterburner.exe"),
  FindTool 'RTSS' @("$pf86\RivaTuner Statistics Server\RTSS.exe","$pf\RivaTuner Statistics Server\RTSS.exe"),
  FindTool 'ISLC' @("$pf\ISLC\Intelligent standby list cleaner ISLC.exe","$pf86\ISLC\Intelligent standby list cleaner ISLC.exe"),
  FindTool 'LatencyMon' @("$pf\LatencyMon\LatMon.exe","$pf86\LatencyMon\LatMon.exe"),
  FindTool 'Fan Control' @("$la\Programs\FanControl\FanControl.exe","$pf\Fan Control\FanControl.exe"),
  FindTool 'System Informer' @("$pf\SystemInformer\SystemInformer.exe","$pf86\SystemInformer\SystemInformer.exe"),
  FindTool 'PresentMon' @("$pf\Intel\PresentMon\PresentMon.exe","$pf\PresentMon\PresentMon.exe"),
  FindTool 'NVIDIA Profile Inspector' @("$ToolRoot\nvidiaProfileInspector.exe","$pf\NVIDIA Profile Inspector\nvidiaProfileInspector.exe"),
  FindTool 'DDU' @("$ToolRoot\DDU\Display Driver Uninstaller.exe")
 )
}
function InstalledApps{
 $out=@();foreach ($r in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*','HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*')){try{$out+=Get-ItemProperty $r -ErrorAction SilentlyContinue|Where-Object DisplayName|Select-Object DisplayName,DisplayVersion,Publisher,InstallLocation}catch{}}
 @($out|Sort-Object DisplayName -Unique)
}
function GetNvidia{
 try{$line=& nvidia-smi --query-gpu=name,driver_version,memory.total,pci.bus_id,temperature.gpu,utilization.gpu,power.draw,clocks.current.graphics --format=csv,noheader,nounits 2>$null|Select-Object -First 1;if ($LASTEXITCODE-eq 0 -and $line){$a=$line -split ','|ForEach-Object{$_.Trim()};return [pscustomobject]@{Name=$a[0];Driver=$a[1];VRAM_MB=[int]$a[2];Bus=$a[3];Temp_C=[double]$a[4];Util=[double]$a[5];Power_W=[double]$a[6];Clock_MHz=[double]$a[7]}}}catch{};return $null
}
function GetStartup{
 $x=@();foreach ($p in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Run','HKLM:\Software\Microsoft\Windows\CurrentVersion\Run','HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run')){if (Test-Path $p){$o=Get-ItemProperty $p;$o.PSObject.Properties|Where-Object{$_.Name-notmatch'^PS'}|ForEach-Object{$x+=[pscustomobject]@{Source=$p;Name=$_.Name;Command=[string]$_.Value}}}}
 try{$x+=Get-CimInstance Win32_StartupCommand|Select-Object @{n='Source';e={'WMI'}},Name,Command}catch{};@($x|Sort-Object Name -Unique)
}
function GetDrivers{
 try{Get-CimInstance Win32_PnPSignedDriver|Where-Object{$_.DeviceClass-in @('DISPLAY','NET','MEDIA','HDC','SCSIADAPTER','USB')}|Select-Object DeviceName,DeviceClass,DriverVersion,DriverDate,Manufacturer}catch{@()}
}
function GetTasks{
 try{Get-ScheduledTask|Where-Object{$_.State-ne'Disabled'}|Where-Object{$_.Triggers.Count-gt 0}|Select-Object TaskName,TaskPath,State}catch{@()}
}
function GetNet{
 try{Get-NetAdapter -Physical|Select-Object Name,InterfaceDescription,Status,LinkSpeed,MacAddress,DriverInformation}catch{@()}
}
function BlackScan{
 Log 'BLACKSCAN started...';$ts=Get-Date -Format 'yyyyMMdd_HHmmss';$json=Join-Path $ScanRoot "D7_BLACKSCAN_$ts.json";$txt=Join-Path $ScanRoot "D7_BLACKSCAN_$ts.txt"
 $os=try{Get-CimInstance Win32_OperatingSystem}catch{$null};$cpu=try{Get-CimInstance Win32_Processor|Select-Object -First 1}catch{$null};$board=try{Get-CimInstance Win32_BaseBoard|Select-Object -First 1}catch{$null};$bios=try{Get-CimInstance Win32_BIOS|Select-Object -First 1}catch{$null};$ram=try{Get-CimInstance Win32_PhysicalMemory|Select-Object Manufacturer,PartNumber,Capacity,Speed,ConfiguredClockSpeed}catch{@()};$disks=try{Get-PhysicalDisk|Select-Object FriendlyName,MediaType,BusType,HealthStatus,OperationalStatus,Size}catch{@()};$page=try{Get-CimInstance Win32_PageFileUsage|Select-Object Name,AllocatedBaseSize,CurrentUsage,PeakUsage}catch{@()};$vbs=try{Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard}catch{$null}
 $nvidia=GetNvidia;$tools=ToolInventory;$startup=GetStartup;$drivers=GetDrivers;$apps=InstalledApps;$tasks=GetTasks;$net=GetNet;$hags=RegGet 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' 'HwSchMode';$gm=RegGet 'HKCU:\Software\Microsoft\GameBar' 'AutoGameModeEnabled';$dvr=RegGet 'HKCU:\System\GameConfigStore' 'GameDVR_Enabled';$power=ActivePower
 $obj=[ordered]@{Schema='D7.BLACKSCAN.v2';Generated=(Get-Date).ToString('o');Windows=[ordered]@{Caption=$os.Caption;Version=$os.Version;Build=$os.BuildNumber;InstallDate=$os.InstallDate;LastBoot=$os.LastBootUpTime;SystemDrive=$os.SystemDrive;FreeSystemGB=[math]::Round((Get-PSDrive $env:SystemDrive.TrimEnd(':')).Free/1GB,1)};CPU=$cpu|Select-Object Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed;Motherboard=$board|Select-Object Manufacturer,Product,Version;BIOS=$bios|Select-Object Manufacturer,SMBIOSBIOSVersion,ReleaseDate;RAM=@($ram);GPU=$nvidia;Disks=@($disks);Pagefile=@($page);PowerScheme=$power;HAGS=$hags;GameMode=$gm;GameDVR=$dvr;VBS=if ($vbs){[ordered]@{Status=$vbs.VirtualizationBasedSecurityStatus;Configured=@($vbs.SecurityServicesConfigured);Running=@($vbs.SecurityServicesRunning)}}else{$null};Network=@($net);Drivers=@($drivers);Startup=@($startup);ScheduledTasks=@($tasks);Tools=@($tools);InstalledApps=@($apps)}
 $obj|ConvertTo-Json -Depth 9|Set-Content $json -Encoding UTF8
 $plan=BuildPlan $obj;$script:LastScan=$obj;$script:LastPlan=$plan
 $lines=@('D7 BLACKCORE BLACKSCAN','=====================','Generated: '+$obj.Generated,'CPU: '+$obj.CPU.Name,'GPU: '+$(if ($obj.GPU){$obj.GPU.Name+' | Driver '+$obj.GPU.Driver}else{'N/A'}),'RAM: '+([math]::Round((($obj.RAM|Measure-Object Capacity -Sum).Sum/1GB),1))+' GB','Motherboard: '+$obj.Motherboard.Manufacturer+' '+$obj.Motherboard.Product,'BIOS: '+$obj.BIOS.SMBIOSBIOSVersion,'Power: '+$obj.PowerScheme,'Startup entries: '+$obj.Startup.Count,'Tools detected: '+(@($obj.Tools|Where-Object Installed).Count),'','D7 PLAN','-------')+$plan.Recommendations
 $lines|Set-Content $txt -Encoding UTF8
 [ordered]@{Fingerprint=($obj.CPU.Name+'|'+$obj.Motherboard.Product+'|'+$obj.GPU.Name+'|'+(($obj.RAM|Measure-Object Capacity -Sum).Sum));LastScan=$obj.Generated;Plan=$plan}|ConvertTo-Json -Depth 6|Set-Content $LearnFile -Encoding UTF8
 Log ('BLACKSCAN complete: '+$json);Log ('Plan score: '+$plan.Score+'/100');Start-Process explorer.exe $ScanRoot
 [Windows.Forms.MessageBox]::Show("BLACKSCAN complete.`n`nJSON:`n$json`n`nTXT:`n$txt`n`nSend the JSON file to ChatGPT so D7 can be tuned specifically for this PC.",'D7 BLACKCORE')|Out-Null
}
function BuildPlan($o){
 $r=New-Object System.Collections.Generic.List[string];$score=100;$ramGB=[math]::Round((($o.RAM|Measure-Object Capacity -Sum).Sum/1GB),1)
 if ($ramGB-le16){$r.Add('RAM: 16GB class system. Keep browser tabs/launchers under control while gaming; do not use blind RAM purges.');$score-=8}
 if ($o.GPU){$r.Add('GPU: '+$o.GPU.Name+' on driver '+$o.GPU.Driver+'. Driver changes should be A/B benchmarked, not blindly chased.')}
 if ($o.GameMode.Value-ne1){$r.Add('Enable Windows Game Mode.');$score-=3}else{$r.Add('Game Mode is enabled.')}
 if ($o.GameDVR.Value-ne0){$r.Add('Disable background Game DVR capture unless explicitly needed.');$score-=4}
 if ($o.Startup.Count-gt20){$r.Add('Startup load is high ('+$o.Startup.Count+' entries). Review launchers/updaters first.');$score-=10}
 $installed=@($o.Tools|Where-Object Installed|ForEach-Object Name)
 if ($installed-notcontains'PresentMon'){$r.Add('Install/attach PresentMon for frametime, 1% low and A/B validation.')}
 if ($installed-notcontains'System Informer'){$r.Add('Optional: System Informer for deep process/service/I/O diagnosis.')}
 if ($installed-notcontains'Fan Control'){$r.Add('Optional: Fan Control for temperature-aware sustained boost profiles.')}
 if ($o.VBS -and [int]$o.VBS.Status-gt0){$r.Add('VBS is active. Do NOT disable automatically; benchmark security/performance tradeoff first.')}
 $r.Add('Use D7 Deep Profile only with backup/rollback. Avoid HPET/BCD/timer registry packs, RealTime priority, Defender disabling, pagefile disabling, and random network registry tweaks.')
 [pscustomobject]@{Score=[math]::Max(0,$score);Recommendations=@($r)}
}
function ApplyDeepProfile{
 if (Test-Path $BackupFile){Log 'Deep Profile already active.';return};$regs=New-Object System.Collections.ArrayList;$targets=@(
 @('HKCU:\Software\Microsoft\GameBar','AutoGameModeEnabled','DWord',1),@('HKCU:\Software\Microsoft\GameBar','AllowAutoGameMode','DWord',1),@('HKCU:\System\GameConfigStore','GameDVR_Enabled','DWord',0),@('HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR','AppCaptureEnabled','DWord',0),@('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects','VisualFXSetting','DWord',2),@('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced','TaskbarAnimations','DWord',0),@('HKLM:\SOFTWARE\Policies\Microsoft\Edge','BackgroundModeEnabled','DWord',0),@('HKLM:\SOFTWARE\Policies\Microsoft\Edge','StartupBoostEnabled','DWord',0))
 foreach ($t in $targets){BackupReg $regs $t[0] $t[1] $t[2];RegSetD $t[0] $t[1] ([int]$t[3])}
 BackupReg $regs 'HKCU:\Control Panel\Desktop\WindowMetrics' 'MinAnimate' 'String';RegSetS 'HKCU:\Control Panel\Desktop\WindowMetrics' 'MinAnimate' '0'
 $old=ActivePower;try{& powercfg /S 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c|Out-Null}catch{}
 [ordered]@{Version=$Version;Applied=(Get-Date).ToString('o');Power=$old;Registry=@($regs)}|ConvertTo-Json -Depth 8|Set-Content $BackupFile -Encoding UTF8
 Log 'Deep Gaming+Streaming profile enabled. Backup created.'
}
function RestoreDeepProfile{
 if (-not(Test-Path $BackupFile)){Log 'No Deep Profile backup found.';return};try{$b=Get-Content $BackupFile -Raw|ConvertFrom-Json;foreach ($e in @($b.Registry)){RestoreReg $e};if ($b.Power){& powercfg /S ([string]$b.Power)|Out-Null};Remove-Item $BackupFile -Force;Log 'Deep Profile restored.'}catch{Log ('Restore failed: '+$_.Exception.Message)}
}
function LoadGames{try{if (Test-Path $GamesFile){$script:LearnedGames=@(Get-Content $GamesFile -Raw|ConvertFrom-Json)}}catch{$script:LearnedGames=@()}}
function LearnForeground {
    [Windows.Forms.MessageBox]::Show('Press OK, switch to the game within 4 seconds.','D7 BLACKCORE') | Out-Null
    $form.Hide()
    try {
        Start-Sleep -Seconds 4
        if (-not ('D7Foreground.NativeMethods' -as [type])) {
            $code = @'
using System;
using System.Runtime.InteropServices;
namespace D7Foreground {
    public static class NativeMethods {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}
'@
            Add-Type -TypeDefinition $code -ErrorAction Stop
        }

        $h = [D7Foreground.NativeMethods]::GetForegroundWindow()
        [uint32]$processId = 0
        [void][D7Foreground.NativeMethods]::GetWindowThreadProcessId($h,[ref]$processId)
        if ($processId -le 0) { throw 'Foreground process could not be resolved.' }

        $p = Get-Process -Id $processId -ErrorAction Stop
        $path = $p.Path
        if (-not $path) { throw 'Foreground process path is unavailable. Try running D7 as administrator.' }

        $existing = @($script:LearnedGames | Where-Object { $_.Path -eq $path }).Count -gt 0
        if (-not $existing) {
            $script:LearnedGames += [pscustomobject]@{
                Name  = $p.ProcessName
                Path  = $path
                Added = (Get-Date).ToString('o')
            }
            $script:LearnedGames | ConvertTo-Json -Depth 5 | Set-Content $GamesFile -Encoding UTF8
            Log ('Learned game: '+$p.ProcessName+' | '+$path)
        } else {
            Log ('Game already learned: '+$p.ProcessName)
        }
        [Windows.Forms.MessageBox]::Show(('Game learned: '+$p.ProcessName),'D7 BLACKCORE') | Out-Null
    }
    catch {
        $msg = $_.Exception.Message
        Log ('Learn game failed: '+$msg)
        [Windows.Forms.MessageBox]::Show(('Learn Game failed: '+$msg+'`n`nKeep the game focused after pressing OK and try again.'),'D7 BLACKCORE') | Out-Null
    }
    finally {
        $form.Show()
        $form.Activate()
    }
}


function SetPrioritySafe($p,$prio){try{if (-not$script:PriorityBackup.ContainsKey($p.Id)){$script:PriorityBackup[$p.Id]=$p.PriorityClass.ToString()};$p.PriorityClass=$prio}catch{}}
function DetectGame{foreach ($g in @($script:LearnedGames)){try{$n=[IO.Path]::GetFileNameWithoutExtension($g.Path);$p=Get-Process -Name $n -ErrorAction SilentlyContinue|Select-Object -First 1;if ($p -and $p.Path-eq$g.Path){return $p}}catch{}};return $null}
function SessionTick{
 $g=DetectGame;if ($g -and -not$script:SessionActive){$script:SessionActive=$true;$script:GamePid=$g.Id;$script:GameName=$g.ProcessName;SetPrioritySafe $g ([Diagnostics.ProcessPriorityClass]::AboveNormal);try{$obs=Get-Process obs64 -ErrorAction SilentlyContinue|Select-Object -First 1;if ($obs){SetPrioritySafe $obs ([Diagnostics.ProcessPriorityClass]::AboveNormal)}}catch{};try{(Get-Process -Id $PID).PriorityClass=[Diagnostics.ProcessPriorityClass]::BelowNormal}catch{};Log ('Adaptive session started: '+$script:GameName)}elseif(-not$g -and $script:SessionActive){foreach ($id in @($script:PriorityBackup.Keys)){try{$p=Get-Process -Id ([int]$id)-ErrorAction Stop;$p.PriorityClass=[Diagnostics.ProcessPriorityClass][Enum]::Parse([Diagnostics.ProcessPriorityClass],$script:PriorityBackup[$id])}catch{}};$script:PriorityBackup=@{};$script:SessionActive=$false;$script:GamePid=0;Log 'Adaptive session ended.'}
}
function WingetReview{if (-not(Get-Command winget -ErrorAction SilentlyContinue)){[Windows.Forms.MessageBox]::Show('winget is not available.','D7 BLACKCORE')|Out-Null;return};Start-Process powershell.exe -ArgumentList '-NoExit','-Command','winget upgrade' -Verb RunAs}
function CheckUpdate{
 if ($script:UpdateBusy){return};$script:UpdateBusy=$true;try{$m=Invoke-RestMethod ($ManifestUrl+'?t='+[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) -TimeoutSec 12;if ([version]$m.version-gt[version]$Version){$ans=[Windows.Forms.MessageBox]::Show("D7 update $($m.version) is available. Install now?",'D7 Online Update',[Windows.Forms.MessageBoxButtons]::YesNo);if ($ans-eq[Windows.Forms.DialogResult]::Yes){InstallUpdate $m}}else{[Windows.Forms.MessageBox]::Show("Latest version installed: $Version",'D7 Online Update')|Out-Null}}catch{Log ('Update check failed: '+$_.Exception.Message)}finally{$script:UpdateBusy=$false}}
function InstallUpdate($m){$d=Join-Path $UpdateRoot ([string]$m.version);Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue;New-Item $d -ItemType Directory -Force|Out-Null;$z=Join-Path $d 'pkg.zip';Invoke-WebRequest $m.packageUrl -OutFile $z -TimeoutSec 120;$h=(Get-FileHash $z -Algorithm SHA256).Hash.ToLower();if ($h-ne([string]$m.sha256).ToLower()){throw'SHA256 mismatch'};Expand-Archive $z (Join-Path $d 'payload') -Force;$new=Get-ChildItem (Join-Path $d 'payload') -Filter D7_Gaming_Engine.exe -Recurse|Select-Object -First 1;$cur=[Environment]::GetCommandLineArgs()[0];$helper=Join-Path $d 'apply.ps1';$body="Wait-Process -Id $PID -ErrorAction SilentlyContinue`nCopy-Item -LiteralPath '$($new.FullName)' -Destination '$cur' -Force`nStart-Process -FilePath '$cur' -Verb RunAs";Set-Content $helper $body -Encoding UTF8;Start-Process powershell.exe -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File',$helper -WindowStyle Hidden;$form.Close()}

# D7 BLACKCORE v2.1 Measurement Core
# Read-mostly diagnostics first: baseline, frametime capture, latency audit, A/B compare.
# All tuning decisions must be justified by measured results and remain reversible.

$MeasureRoot = Join-Path $DataRoot 'Measurements'
New-Item -ItemType Directory -Path $MeasureRoot -Force | Out-Null

function Get-InstalledAppIndex {
    $items = @()
    foreach ($r in @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )) {
        try {
            $items += Get-ItemProperty $r -ErrorAction SilentlyContinue | Where-Object DisplayName | Select-Object DisplayName,DisplayVersion,InstallLocation,UninstallString
        } catch {}
    }
    return @($items | Sort-Object DisplayName -Unique)
}

# D7 BLACKCORE v2.1.1 - fast tool inventory hotfix
# Avoid recursive scans of Program Files. Detection uses known paths, PATH, uninstall registry and scheduled-task actions.

function Resolve-ToolPathFast {
    param($Def,$App,$Task)
    foreach($p in @($Def.Paths)){
        if($p -and (Test-Path -LiteralPath $p)){ try { return (Resolve-Path -LiteralPath $p).Path } catch { return $p } }
    }
    try {
        $cmd=Get-Command $Def.Exe -ErrorAction SilentlyContinue | Select-Object -First 1
        if($cmd -and $cmd.Source -and (Test-Path -LiteralPath $cmd.Source)){ return $cmd.Source }
    } catch {}
    if($App -and $App.InstallLocation){
        try {
            $candidate=Join-Path ([string]$App.InstallLocation).Trim('"') $Def.Exe
            if(Test-Path -LiteralPath $candidate){ return $candidate }
        } catch {}
    }
    if($Task){
        try {
            foreach($a in @($Task.Actions)){
                $exe=[string]$a.Execute
                if($exe){
                    $exe=$exe.Trim('"')
                    if(Test-Path -LiteralPath $exe){ return $exe }
                }
            }
        } catch {}
    }
    return $null
}

function ToolInventory {
    $pf=$env:ProgramFiles; $pf86=${env:ProgramFiles(x86)}; $la=$env:LOCALAPPDATA; $pd=$env:ProgramData
    $apps=Get-InstalledAppIndex
    $tasks=@(); try { $tasks=@(Get-ScheduledTask -ErrorAction SilentlyContinue) } catch {}
    $defs=@(
        @{N='MSI Afterburner';Exe='MSIAfterburner.exe';Paths=@("$pf86\MSI Afterburner\MSIAfterburner.exe","$pf\MSI Afterburner\MSIAfterburner.exe");Match='MSI Afterburner';Task='MSIAfterburner'},
        @{N='RTSS';Exe='RTSS.exe';Paths=@("$pf86\RivaTuner Statistics Server\RTSS.exe","$pf\RivaTuner Statistics Server\RTSS.exe");Match='RivaTuner Statistics Server';Task='RivaTuner'},
        @{N='ISLC';Exe='Intelligent standby list cleaner ISLC.exe';Paths=@("$pf\ISLC\Intelligent standby list cleaner ISLC.exe","$pf86\ISLC\Intelligent standby list cleaner ISLC.exe");Match='Intelligent Standby';Task='Intelligent StandbyList Cleaner'},
        @{N='LatencyMon';Exe='LatMon.exe';Paths=@("$pf\LatencyMon\LatMon.exe","$pf86\LatencyMon\LatMon.exe");Match='LatencyMon';Task='LatencyMon'},
        @{N='Fan Control';Exe='FanControl.exe';Paths=@("$la\Programs\FanControl\FanControl.exe","$pf\Fan Control\FanControl.exe","$pd\FanControl\FanControl.exe");Match='Fan Control';Task='FanControl'},
        @{N='System Informer';Exe='SystemInformer.exe';Paths=@("$pf\SystemInformer\SystemInformer.exe","$pf86\SystemInformer\SystemInformer.exe");Match='System Informer';Task='System Informer'},
        @{N='PresentMon';Exe='PresentMon.exe';Paths=@("$pf\Intel\PresentMon\PresentMon.exe","$pf\PresentMon\PresentMon.exe","$ToolRoot\PresentMon\PresentMon.exe");Match='PresentMon';Task='PresentMon'},
        @{N='NVIDIA Profile Inspector';Exe='nvidiaProfileInspector.exe';Paths=@("$ToolRoot\nvidiaProfileInspector.exe","$pf\NVIDIA Profile Inspector\nvidiaProfileInspector.exe");Match='NVIDIA Profile Inspector';Task='NVIDIA Profile Inspector'},
        @{N='DDU';Exe='Display Driver Uninstaller.exe';Paths=@("$ToolRoot\DDU\Display Driver Uninstaller.exe");Match='Display Driver Uninstaller';Task='Display Driver Uninstaller'},
        @{N='OCCT';Exe='OCCT.exe';Paths=@("$pf\OCCT\OCCT.exe","$pf86\OCCT\OCCT.exe");Match='OCCT';Task='OCCT'},
        @{N='ZenTimings';Exe='ZenTimings.exe';Paths=@("$ToolRoot\ZenTimings\ZenTimings.exe");Match='ZenTimings';Task='ZenTimings'}
    )
    $out=@()
    foreach($d in $defs){
        $app=$apps | Where-Object { $_.DisplayName -like ('*'+$d.Match+'*') } | Select-Object -First 1
        $task=$tasks | Where-Object { $_.TaskName -like ('*'+$d.Task+'*') -or $_.TaskName -like ('*'+$d.N+'*') } | Select-Object -First 1
        $path=Resolve-ToolPathFast -Def $d -App $app -Task $task
        $installed=[bool]($path -or $app -or $task)
        $ver=$null
        if($path){ try { $ver=FileVersion $path } catch {} }
        if(-not $ver -and $app){ $ver=$app.DisplayVersion }
        $by=if($path){'Executable'}elseif($app){'InstalledApps'}elseif($task){'ScheduledTask'}else{'None'}
        $out += [pscustomobject]@{Name=$d.N;Installed=$installed;Path=$path;Version=$ver;DetectedBy=$by}
    }
    return $out
}


function Get-PresentMonPath {
    $t = ToolInventory | Where-Object { $_.Name -eq 'PresentMon' -and $_.Installed } | Select-Object -First 1
    if ($t -and $t.Path -and (Test-Path $t.Path)) { return $t.Path }
    return $null
}

function Get-SystemSample {
    $cpu = $null; $os = $null
    try { $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1 } catch {}
    try { $os = Get-CimInstance Win32_OperatingSystem } catch {}
    $gpu = GetNvidia
    $freeGb = $null
    $usedPct = $null
    if ($os) {
        $freeGb = [math]::Round(([double]$os.FreePhysicalMemory * 1KB / 1GB),2)
        $totalGb = [double]$os.TotalVisibleMemorySize * 1KB / 1GB
        if ($totalGb -gt 0) { $usedPct = [math]::Round((1 - ($freeGb / $totalGb))*100,1) }
    }
    $cpuLoad = $null
    try { $cpuLoad = [double]$cpu.LoadPercentage } catch {}
    return [pscustomobject]@{
        Time=(Get-Date).ToString('o');CPU_Load=$cpuLoad;RAM_FreeGB=$freeGb;RAM_UsedPct=$usedPct;
        GPU_Util=$(if ($gpu){$gpu.Util}else{$null});GPU_TempC=$(if ($gpu){$gpu.Temp_C}else{$null});GPU_PowerW=$(if ($gpu){$gpu.Power_W}else{$null});GPU_ClockMHz=$(if ($gpu){$gpu.Clock_MHz}else{$null})
    }
}

function Get-RecentStabilityEvents {
    param([int]$Minutes=30)
    $start=(Get-Date).AddMinutes(-$Minutes)
    $events=@()
    try {
        $events += Get-WinEvent -FilterHashtable @{LogName='System';StartTime=$start;Id=18,19,20,46} -ErrorAction SilentlyContinue | Where-Object { $_.ProviderName -match 'WHEA' } | Select-Object TimeCreated,Id,ProviderName,LevelDisplayName,Message
    } catch {}
    try {
        $events += Get-WinEvent -FilterHashtable @{LogName='System';StartTime=$start;Id=4101} -ErrorAction SilentlyContinue | Select-Object TimeCreated,Id,ProviderName,LevelDisplayName,Message
    } catch {}
    try {
        $events += Get-WinEvent -FilterHashtable @{LogName='System';StartTime=$start;Id=41,6008} -ErrorAction SilentlyContinue | Select-Object TimeCreated,Id,ProviderName,LevelDisplayName,Message
    } catch {}
    return @($events | Sort-Object TimeCreated)
}

function Get-InterruptAudit {
    $items=@()
    try {
        $devs=Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.Class -in @('Display','Net','MEDIA','USB') }
        foreach ($d in $devs){
            $msi=$null; $affinityPolicy=$null
            $base='HKLM:\SYSTEM\CurrentControlSet\Enum\'+$d.InstanceId+'\Device Parameters\Interrupt Management'
            try { $msi=(Get-ItemProperty ($base+'\MessageSignaledInterruptProperties') -Name MSISupported -ErrorAction Stop).MSISupported } catch {}
            try { $affinityPolicy=(Get-ItemProperty ($base+'\Affinity Policy') -Name DevicePolicy -ErrorAction Stop).DevicePolicy } catch {}
            $items += [pscustomobject]@{FriendlyName=$d.FriendlyName;Class=$d.Class;InstanceId=$d.InstanceId;MSISupported=$msi;AffinityPolicy=$affinityPolicy}
        }
    } catch {}
    return $items
}

function Get-NetworkDeepAudit {
    $rows=@()
    try {
        foreach ($a in @(Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object Status -eq 'Up')){
            $adv=@();$rss=$null
            try { $adv=@(Get-NetAdapterAdvancedProperty -Name $a.Name -ErrorAction SilentlyContinue | Select-Object DisplayName,DisplayValue,RegistryKeyword,RegistryValue) } catch {}
            try { $rss=Get-NetAdapterRss -Name $a.Name -ErrorAction SilentlyContinue | Select-Object Enabled,NumberOfReceiveQueues,Profile,BaseProcessorGroup,MaxProcessorGroup } catch {}
            $rows += [pscustomobject]@{Name=$a.Name;Description=$a.InterfaceDescription;LinkSpeed=$a.LinkSpeed;DriverInformation=$a.DriverInformation;RSS=$rss;Advanced=$adv}
        }
    } catch {}
    return $rows
}

function Get-XperfStatus {
    $x = Get-Command xperf.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($x){ return [pscustomobject]@{Available=$true;Path=$x.Source} }
    return [pscustomobject]@{Available=$false;Path=$null}
}

function Invoke-PresentMonCapture {
    param([int]$ProcessId,[int]$Seconds=20,[string]$OutputCsv)
    $pm=Get-PresentMonPath
    if (-not $pm){ return [pscustomobject]@{Available=$false;Success=$false;Reason='PresentMon not detected';Csv=$null} }
    try {
        $help=(& $pm --help 2>&1 | Out-String)
        if ($help -match '--process_id'){
            & $pm --process_id $ProcessId --timed $Seconds --output_file $OutputCsv 2>$null | Out-Null
        } else {
            & $pm -process_id $ProcessId -timed $Seconds -output_file $OutputCsv 2>$null | Out-Null
        }
        if (Test-Path $OutputCsv){ return [pscustomobject]@{Available=$true;Success=$true;Reason=$null;Csv=$OutputCsv} }
        return [pscustomobject]@{Available=$true;Success=$false;Reason='No CSV produced';Csv=$null}
    } catch {
        return [pscustomobject]@{Available=$true;Success=$false;Reason=$_.Exception.Message;Csv=$null}
    }
}

function Get-Percentile {
    param([double[]]$Values,[double]$P)
    if (-not $Values -or $Values.Count -eq 0){ return $null }
    $s=@($Values | Sort-Object)
    $i=[math]::Ceiling(($P/100.0)*$s.Count)-1
    if ($i -lt 0){$i=0}; if ($i -ge $s.Count){$i=$s.Count-1}
    return [double]$s[$i]
}

function Analyze-PresentMonCsv {
    param([string]$Csv)
    if (-not(Test-Path $Csv)){ return $null }
    try { $rows=@(Import-Csv $Csv) } catch { return $null }
    if ($rows.Count -lt 10){ return [pscustomobject]@{Frames=$rows.Count;Valid=$false} }
    $props=@($rows[0].PSObject.Properties.Name)
    $candidates=@('MsBetweenPresents','CPUFrameTime','FrameTime','DisplayedTime','MsUntilRenderComplete')
    $col=$candidates | Where-Object { $props -contains $_ } | Select-Object -First 1
    if (-not $col){ return [pscustomobject]@{Frames=$rows.Count;Valid=$false;Columns=$props} }
    $ft=@()
    foreach ($r in $rows){
        $v=0.0
        if ([double]::TryParse([string]$r.$col,[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$v)){
            if ($v -gt 0 -and $v -lt 1000){$ft+=$v}
        }
    }
    if ($ft.Count -lt 10){ return [pscustomobject]@{Frames=$rows.Count;Valid=$false;FrameTimeColumn=$col} }
    $avgMs=($ft|Measure-Object -Average).Average
    $fps=@($ft|ForEach-Object{1000.0/$_})
    $avgFps=($fps|Measure-Object -Average).Average
    $sortedFps=@($fps|Sort-Object)
    $n1=[math]::Max(1,[math]::Ceiling($sortedFps.Count*0.01));$n01=[math]::Max(1,[math]::Ceiling($sortedFps.Count*0.001))
    $low1=($sortedFps|Select-Object -First $n1|Measure-Object -Average).Average
    $low01=($sortedFps|Select-Object -First $n01|Measure-Object -Average).Average
    $median=Get-Percentile -Values $ft -P 50;$threshold=[math]::Max($median*1.5,$median+5.0);$stutters=@($ft|Where-Object{$_-gt$threshold}).Count
    return [pscustomobject]@{Valid=$true;Frames=$ft.Count;FrameTimeColumn=$col;AvgFPS=[math]::Round($avgFps,2);Low1FPS=[math]::Round($low1,2);Low01FPS=[math]::Round($low01,2);AvgFrameMs=[math]::Round($avgMs,3);P95FrameMs=[math]::Round((Get-Percentile $ft 95),3);P99FrameMs=[math]::Round((Get-Percentile $ft 99),3);Stutters=$stutters;StutterThresholdMs=[math]::Round($threshold,3)}
}

function Run-MeasurementBaseline {
    Log 'MEASUREMENT CORE: baseline capture started.'
    $ts=Get-Date -Format 'yyyyMMdd_HHmmss';$dir=Join-Path $MeasureRoot $ts;New-Item $dir -ItemType Directory -Force|Out-Null
    $before=Get-RecentStabilityEvents -Minutes 30
    $samples=@();1..10|ForEach-Object{$samples+=Get-SystemSample;[Windows.Forms.Application]::DoEvents();Start-Sleep -Milliseconds 750}
    $game=DetectGame;$pmResult=$null;$frame=$null
    if ($game){
        $csv=Join-Path $dir 'presentmon.csv';$pmResult=Invoke-PresentMonCapture -ProcessId $game.Id -Seconds 20 -OutputCsv $csv
        if ($pmResult.Success){$frame=Analyze-PresentMonCsv $csv}
    } else { $pmResult=[pscustomobject]@{Available=[bool](Get-PresentMonPath);Success=$false;Reason='No learned game is currently running';Csv=$null} }
    $after=Get-RecentStabilityEvents -Minutes 1
    $report=[ordered]@{Schema='D7.MEASUREMENT.v1';Generated=(Get-Date).ToString('o');Game=$(if ($game){[ordered]@{Name=$game.ProcessName;Pid=$game.Id;Path=$game.Path}}else{$null});SystemSamples=@($samples);PresentMon=$pmResult;FrameAnalysis=$frame;StabilityBefore=@($before);StabilityDuring=@($after);InterruptAudit=@(Get-InterruptAudit);NetworkAudit=@(Get-NetworkDeepAudit);Xperf=Get-XperfStatus;Policy=[ordered]@{Mode='measure-first';WritesApplied=$false;RollbackRequired=$false}}
    $json=Join-Path $dir 'measurement.json';$report|ConvertTo-Json -Depth 10|Set-Content $json -Encoding UTF8
    $script:LastMeasurement=$report
    Log ('MEASUREMENT CORE: saved '+$json)
    if ($frame -and $frame.Valid){Log ('FRAME RESULT: Avg '+$frame.AvgFPS+' FPS | 1% '+$frame.Low1FPS+' | 0.1% '+$frame.Low01FPS+' | P99 '+$frame.P99FrameMs+'ms | stutters '+$frame.Stutters)}else{Log ('FRAME RESULT: '+$pmResult.Reason)}
    Start-Process explorer.exe $dir
    return $report
}

function Run-LatencyAudit {
    Log 'LATENCY LAB: read-only audit started.'
    $ts=Get-Date -Format 'yyyyMMdd_HHmmss';$out=Join-Path $MeasureRoot ('latency_'+$ts+'.json')
    $obj=[ordered]@{Schema='D7.LATENCY.v1';Generated=(Get-Date).ToString('o');Interrupts=@(Get-InterruptAudit);Network=@(Get-NetworkDeepAudit);Stability=@(Get-RecentStabilityEvents -Minutes 60);Xperf=Get-XperfStatus;Notes=@('MSI/Affinity values are read-only in this phase.','No interrupt affinity or network offload values are changed automatically.','ETW DPC/ISR trace will activate only when Windows Performance Toolkit xperf is available.')}
    $obj|ConvertTo-Json -Depth 10|Set-Content $out -Encoding UTF8
    Log ('LATENCY LAB: '+$out)
    Start-Process notepad.exe $out
    return $obj
}

function Compare-LastMeasurements {
    $files=@(Get-ChildItem $MeasureRoot -Filter measurement.json -Recurse -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 2)
    if ($files.Count -lt 2){[Windows.Forms.MessageBox]::Show('Need two measurement runs first.','D7 A/B Compare')|Out-Null;return}
    try{$a=Get-Content $files[1].FullName -Raw|ConvertFrom-Json;$b=Get-Content $files[0].FullName -Raw|ConvertFrom-Json}catch{[Windows.Forms.MessageBox]::Show('Could not read measurement files.','D7 A/B Compare')|Out-Null;return}
    $lines=New-Object System.Collections.Generic.List[string]
    $lines.Add('A/B MEASUREMENT COMPARISON')
    $lines.Add('A: '+$a.Generated)
    $lines.Add('B: '+$b.Generated)
    if ($a.FrameAnalysis.Valid -and $b.FrameAnalysis.Valid){
        $lines.Add(('Avg FPS: {0} -> {1} ({2:+0.00;-0.00;0.00})' -f $a.FrameAnalysis.AvgFPS,$b.FrameAnalysis.AvgFPS,([double]$b.FrameAnalysis.AvgFPS-[double]$a.FrameAnalysis.AvgFPS)))
        $lines.Add(('1% Low: {0} -> {1} ({2:+0.00;-0.00;0.00})' -f $a.FrameAnalysis.Low1FPS,$b.FrameAnalysis.Low1FPS,([double]$b.FrameAnalysis.Low1FPS-[double]$a.FrameAnalysis.Low1FPS)))
        $lines.Add(('0.1% Low: {0} -> {1} ({2:+0.00;-0.00;0.00})' -f $a.FrameAnalysis.Low01FPS,$b.FrameAnalysis.Low01FPS,([double]$b.FrameAnalysis.Low01FPS-[double]$a.FrameAnalysis.Low01FPS)))
        $lines.Add(('P99 ms: {0} -> {1}' -f $a.FrameAnalysis.P99FrameMs,$b.FrameAnalysis.P99FrameMs))
        $lines.Add(('Stutters: {0} -> {1}' -f $a.FrameAnalysis.Stutters,$b.FrameAnalysis.Stutters))
    } else {$lines.Add('Frame A/B unavailable. Ensure a learned game is running and PresentMon is detected.')}
    [Windows.Forms.MessageBox]::Show(($lines -join "`n"),'D7 BLACKCORE A/B')|Out-Null
}


LoadGames
$form=New-Object Windows.Forms.Form;$form.Text='D7 BLACKCORE v2.1.3';$form.Size=New-Object Drawing.Size(1120,720);$form.StartPosition='CenterScreen';$form.BackColor=[Drawing.Color]::FromArgb(8,10,12);$form.ForeColor=[Drawing.Color]::White
$title=New-Object Windows.Forms.Label;$title.Text='D7 BLACKCORE - GAMING SYSTEM ORCHESTRATOR';$title.Font=New-Object Drawing.Font('Consolas',20,[Drawing.FontStyle]::Bold);$title.ForeColor=[Drawing.Color]::LawnGreen;$title.AutoSize=$true;$title.Location=New-Object Drawing.Point(30,25);$form.Controls.Add($title)
$sub=New-Object Windows.Forms.Label;$sub.Text='BLACKSCAN -> PLAN -> APPLY -> MEASURE -> LEARN -> ROLLBACK';$sub.Font=New-Object Drawing.Font('Consolas',10);$sub.ForeColor=[Drawing.Color]::DeepSkyBlue;$sub.AutoSize=$true;$sub.Location=New-Object Drawing.Point(33,70);$form.Controls.Add($sub)
function Btn($t,$x,$y,$w=180){$b=New-Object Windows.Forms.Button;$b.Text=$t;$b.Size=New-Object Drawing.Size($w,45);$b.Location=New-Object Drawing.Point($x,$y);$b.FlatStyle='Flat';$b.BackColor=[Drawing.Color]::FromArgb(25,25,25);$b.ForeColor=[Drawing.Color]::White;$form.Controls.Add($b);return $b}
$bScan=Btn 'BLACKSCAN FULL' 30 115 190;$bPlan=Btn 'SHOW D7 PLAN' 235 115 190;$bApply=Btn 'APPLY DEEP PROFILE' 440 115 190;$bRestore=Btn 'ROLLBACK' 645 115 150;$bLearn=Btn 'LEARN GAME' 810 115 130;$bUpdate=Btn 'ONLINE UPDATE' 950 115 130
$bTools=Btn 'TOOL HUB' 30 170 190;$bWinget=Btn 'REVIEW APP UPDATES' 235 170 190;$bFolder=Btn 'OPEN SCANS' 440 170 190;$bMeasure=Btn 'MEASURE BASELINE' 645 170 190;$bLatency=Btn 'LATENCY LAB' 850 170 120;$bCompare=Btn 'A/B COMPARE' 980 170 100
$stat=New-Object Windows.Forms.Label;$stat.Text='State: IDLE | waiting for BLACKSCAN';$stat.Font=New-Object Drawing.Font('Consolas',11,[Drawing.FontStyle]::Bold);$stat.ForeColor=[Drawing.Color]::Gold;$stat.AutoSize=$true;$stat.Location=New-Object Drawing.Point(30,235);$form.Controls.Add($stat)
$script:LogBox=New-Object Windows.Forms.TextBox;$script:LogBox.Multiline=$true;$script:LogBox.ReadOnly=$true;$script:LogBox.ScrollBars='Vertical';$script:LogBox.BackColor=[Drawing.Color]::Black;$script:LogBox.ForeColor=[Drawing.Color]::White;$script:LogBox.Font=New-Object Drawing.Font('Consolas',9);$script:LogBox.Location=New-Object Drawing.Point(30,275);$script:LogBox.Size=New-Object Drawing.Size(1040,360);$form.Controls.Add($script:LogBox)
$bScan.Add_Click({BlackScan;$stat.Text='State: BLACKSCAN COMPLETE';$stat.ForeColor=[Drawing.Color]::LawnGreen})
$bPlan.Add_Click({if (-not$script:LastPlan -and(Test-Path $LearnFile)){try{$x=Get-Content $LearnFile -Raw|ConvertFrom-Json;$script:LastPlan=$x.Plan}catch{}};if ($script:LastPlan){[Windows.Forms.MessageBox]::Show(($script:LastPlan.Recommendations -join "`n`n"),('D7 Plan - Score '+$script:LastPlan.Score+'/100'))|Out-Null}else{[Windows.Forms.MessageBox]::Show('Run BLACKSCAN first.','D7 BLACKCORE')|Out-Null}})
$bApply.Add_Click({ApplyDeepProfile;$stat.Text='State: DEEP PROFILE ACTIVE';$stat.ForeColor=[Drawing.Color]::LawnGreen})
$bRestore.Add_Click({RestoreDeepProfile;$stat.Text='State: ROLLED BACK';$stat.ForeColor=[Drawing.Color]::Gold})
$bLearn.Add_Click({LearnForeground})
$bUpdate.Add_Click({CheckUpdate})
$bWinget.Add_Click({WingetReview})
$bFolder.Add_Click({Start-Process explorer.exe $ScanRoot})
$bTools.Add_Click({$t=ToolInventory;$s=$t|ForEach-Object{('{0,-28} {1,-10} {2}'-f$_.Name,$_.Installed,$_.Version)};[Windows.Forms.MessageBox]::Show(($s-join"`n"),'D7 Tool Hub')|Out-Null})
$bMeasure.Add_Click({$stat.Text='State: MEASURING';$stat.ForeColor=[Drawing.Color]::DeepSkyBlue;try{[void](Run-MeasurementBaseline);$stat.Text='State: MEASUREMENT COMPLETE';$stat.ForeColor=[Drawing.Color]::LawnGreen}catch{$msg=$_.Exception.Message;Log ('Measurement failed: '+$msg);$stat.Text='State: MEASUREMENT ERROR';$stat.ForeColor=[Drawing.Color]::OrangeRed;[Windows.Forms.MessageBox]::Show(('Measurement failed: '+$msg),'D7 BLACKCORE')|Out-Null}})
$bLatency.Add_Click({try{[void](Run-LatencyAudit)}catch{$msg=$_.Exception.Message;Log ('Latency Lab failed: '+$msg);[Windows.Forms.MessageBox]::Show(('Latency Lab failed: '+$msg),'D7 BLACKCORE')|Out-Null}})
$bCompare.Add_Click({try{Compare-LastMeasurements}catch{$msg=$_.Exception.Message;Log ('A/B Compare failed: '+$msg);[Windows.Forms.MessageBox]::Show(('A/B Compare failed: '+$msg),'D7 BLACKCORE')|Out-Null}})
$timer=New-Object Windows.Forms.Timer;$timer.Interval=2500;$timer.Add_Tick({SessionTick;if ($script:SessionActive){$stat.Text='State: GAMING | '+$script:GameName;$stat.ForeColor=[Drawing.Color]::LawnGreen}});$timer.Start()
Log ('D7 BLACKCORE v'+$Version+' started.');Log 'Policy: reversible changes only. No HPET/BCD/timer hacks, no RealTime priority, no Defender/pagefile disabling.'
[void]$form.ShowDialog()
