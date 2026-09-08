Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$Version='2.0.0'
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

function Log([string]$s){$l='[{0}] {1}'-f(Get-Date -Format 'HH:mm:ss'),$s;try{Add-Content $LogFile $l -Encoding UTF8}catch{};if($script:LogBox){$script:LogBox.AppendText($l+[Environment]::NewLine);$script:LogBox.SelectionStart=$script:LogBox.TextLength;$script:LogBox.ScrollToCaret()}}
function RegGet([string]$p,[string]$n){try{$x=Get-ItemProperty -Path $p -Name $n -ErrorAction Stop;[pscustomobject]@{Exists=$true;Value=$x.$n}}catch{[pscustomobject]@{Exists=$false;Value=$null}}}
function RegSetD([string]$p,[string]$n,[int]$v){New-Item $p -Force|Out-Null;New-ItemProperty $p -Name $n -PropertyType DWord -Value $v -Force|Out-Null}
function RegSetS([string]$p,[string]$n,[string]$v){New-Item $p -Force|Out-Null;New-ItemProperty $p -Name $n -PropertyType String -Value $v -Force|Out-Null}
function ActivePower(){try{$s=& powercfg /GETACTIVESCHEME 2>&1|Out-String;if($s-match'([0-9a-fA-F-]{36})'){return $matches[1]}}catch{};return $null}
function BackupReg($list,$p,$n,$t){$g=RegGet $p $n;[void]$list.Add([pscustomobject]@{Path=$p;Name=$n;Type=$t;Exists=$g.Exists;Value=$g.Value})}
function RestoreReg($e){try{if($e.Exists){if($e.Type-eq'DWord'){RegSetD $e.Path $e.Name ([int]$e.Value)}else{RegSetS $e.Path $e.Name ([string]$e.Value)}}else{Remove-ItemProperty -Path $e.Path -Name $e.Name -Force -ErrorAction SilentlyContinue}}catch{}}
function FileVersion($p){try{([Diagnostics.FileVersionInfo]::GetVersionInfo($p)).FileVersion}catch{return $null}}

function FindTool([string]$name,[string[]]$paths){foreach($p in $paths){if(Test-Path $p){return[pscustomobject]@{Name=$name;Installed=$true;Path=$p;Version=(FileVersion $p)}}};return[pscustomobject]@{Name=$name;Installed=$false;Path=$null;Version=$null}}
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
 $out=@();foreach($r in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*','HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*')){try{$out+=Get-ItemProperty $r -ErrorAction SilentlyContinue|Where-Object DisplayName|Select-Object DisplayName,DisplayVersion,Publisher,InstallLocation}catch{}}
 @($out|Sort-Object DisplayName -Unique)
}
function GetNvidia{
 try{$line=& nvidia-smi --query-gpu=name,driver_version,memory.total,pci.bus_id,temperature.gpu,utilization.gpu,power.draw,clocks.current.graphics --format=csv,noheader,nounits 2>$null|Select-Object -First 1;if($LASTEXITCODE-eq 0 -and $line){$a=$line -split ','|ForEach-Object{$_.Trim()};return[pscustomobject]@{Name=$a[0];Driver=$a[1];VRAM_MB=[int]$a[2];Bus=$a[3];Temp_C=[double]$a[4];Util=[double]$a[5];Power_W=[double]$a[6];Clock_MHz=[double]$a[7]}}}catch{};return $null
}
function GetStartup{
 $x=@();foreach($p in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Run','HKLM:\Software\Microsoft\Windows\CurrentVersion\Run','HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run')){if(Test-Path $p){$o=Get-ItemProperty $p;$o.PSObject.Properties|Where-Object{$_.Name-notmatch'^PS'}|ForEach-Object{$x+=[pscustomobject]@{Source=$p;Name=$_.Name;Command=[string]$_.Value}}}}
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
 $obj=[ordered]@{Schema='D7.BLACKSCAN.v2';Generated=(Get-Date).ToString('o');Windows=[ordered]@{Caption=$os.Caption;Version=$os.Version;Build=$os.BuildNumber;InstallDate=$os.InstallDate;LastBoot=$os.LastBootUpTime;SystemDrive=$os.SystemDrive;FreeSystemGB=[math]::Round((Get-PSDrive $env:SystemDrive.TrimEnd(':')).Free/1GB,1)};CPU=$cpu|Select-Object Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed;Motherboard=$board|Select-Object Manufacturer,Product,Version;BIOS=$bios|Select-Object Manufacturer,SMBIOSBIOSVersion,ReleaseDate;RAM=@($ram);GPU=$nvidia;Disks=@($disks);Pagefile=@($page);PowerScheme=$power;HAGS=$hags;GameMode=$gm;GameDVR=$dvr;VBS=if($vbs){[ordered]@{Status=$vbs.VirtualizationBasedSecurityStatus;Configured=@($vbs.SecurityServicesConfigured);Running=@($vbs.SecurityServicesRunning)}}else{$null};Network=@($net);Drivers=@($drivers);Startup=@($startup);ScheduledTasks=@($tasks);Tools=@($tools);InstalledApps=@($apps)}
 $obj|ConvertTo-Json -Depth 9|Set-Content $json -Encoding UTF8
 $plan=BuildPlan $obj;$script:LastScan=$obj;$script:LastPlan=$plan
 $lines=@('D7 BLACKCORE BLACKSCAN','=====================','Generated: '+$obj.Generated,'CPU: '+$obj.CPU.Name,'GPU: '+$(if($obj.GPU){$obj.GPU.Name+' | Driver '+$obj.GPU.Driver}else{'N/A'}),'RAM: '+([math]::Round((($obj.RAM|Measure-Object Capacity -Sum).Sum/1GB),1))+' GB','Motherboard: '+$obj.Motherboard.Manufacturer+' '+$obj.Motherboard.Product,'BIOS: '+$obj.BIOS.SMBIOSBIOSVersion,'Power: '+$obj.PowerScheme,'Startup entries: '+$obj.Startup.Count,'Tools detected: '+(@($obj.Tools|Where-Object Installed).Count),'','D7 PLAN','-------')+$plan.Recommendations
 $lines|Set-Content $txt -Encoding UTF8
 [ordered]@{Fingerprint=($obj.CPU.Name+'|'+$obj.Motherboard.Product+'|'+$obj.GPU.Name+'|'+(($obj.RAM|Measure-Object Capacity -Sum).Sum));LastScan=$obj.Generated;Plan=$plan}|ConvertTo-Json -Depth 6|Set-Content $LearnFile -Encoding UTF8
 Log ('BLACKSCAN complete: '+$json);Log ('Plan score: '+$plan.Score+'/100');Start-Process explorer.exe $ScanRoot
 [Windows.Forms.MessageBox]::Show("BLACKSCAN complete.`n`nJSON:`n$json`n`nTXT:`n$txt`n`nSend the JSON file to ChatGPT so D7 can be tuned specifically for this PC.",'D7 BLACKCORE')|Out-Null
}
function BuildPlan($o){
 $r=New-Object System.Collections.Generic.List[string];$score=100;$ramGB=[math]::Round((($o.RAM|Measure-Object Capacity -Sum).Sum/1GB),1)
 if($ramGB-le16){$r.Add('RAM: 16GB class system. Keep browser tabs/launchers under control while gaming; do not use blind RAM purges.');$score-=8}
 if($o.GPU){$r.Add('GPU: '+$o.GPU.Name+' on driver '+$o.GPU.Driver+'. Driver changes should be A/B benchmarked, not blindly chased.')}
 if($o.GameMode.Value-ne1){$r.Add('Enable Windows Game Mode.');$score-=3}else{$r.Add('Game Mode is enabled.')}
 if($o.GameDVR.Value-ne0){$r.Add('Disable background Game DVR capture unless explicitly needed.');$score-=4}
 if($o.Startup.Count-gt20){$r.Add('Startup load is high ('+$o.Startup.Count+' entries). Review launchers/updaters first.');$score-=10}
 $installed=@($o.Tools|Where-Object Installed|ForEach-Object Name)
 if($installed-notcontains'PresentMon'){$r.Add('Install/attach PresentMon for frametime, 1% low and A/B validation.')}
 if($installed-notcontains'System Informer'){$r.Add('Optional: System Informer for deep process/service/I/O diagnosis.')}
 if($installed-notcontains'Fan Control'){$r.Add('Optional: Fan Control for temperature-aware sustained boost profiles.')}
 if($o.VBS -and [int]$o.VBS.Status-gt0){$r.Add('VBS is active. Do NOT disable automatically; benchmark security/performance tradeoff first.')}
 $r.Add('Use D7 Deep Profile only with backup/rollback. Avoid HPET/BCD/timer registry packs, RealTime priority, Defender disabling, pagefile disabling, and random network registry tweaks.')
 [pscustomobject]@{Score=[math]::Max(0,$score);Recommendations=@($r)}
}
function ApplyDeepProfile{
 if(Test-Path $BackupFile){Log 'Deep Profile already active.';return};$regs=New-Object System.Collections.ArrayList;$targets=@(
 @('HKCU:\Software\Microsoft\GameBar','AutoGameModeEnabled','DWord',1),@('HKCU:\Software\Microsoft\GameBar','AllowAutoGameMode','DWord',1),@('HKCU:\System\GameConfigStore','GameDVR_Enabled','DWord',0),@('HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR','AppCaptureEnabled','DWord',0),@('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects','VisualFXSetting','DWord',2),@('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced','TaskbarAnimations','DWord',0),@('HKLM:\SOFTWARE\Policies\Microsoft\Edge','BackgroundModeEnabled','DWord',0),@('HKLM:\SOFTWARE\Policies\Microsoft\Edge','StartupBoostEnabled','DWord',0))
 foreach($t in $targets){BackupReg $regs $t[0] $t[1] $t[2];RegSetD $t[0] $t[1] ([int]$t[3])}
 BackupReg $regs 'HKCU:\Control Panel\Desktop\WindowMetrics' 'MinAnimate' 'String';RegSetS 'HKCU:\Control Panel\Desktop\WindowMetrics' 'MinAnimate' '0'
 $old=ActivePower;try{& powercfg /S 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c|Out-Null}catch{}
 [ordered]@{Version=$Version;Applied=(Get-Date).ToString('o');Power=$old;Registry=@($regs)}|ConvertTo-Json -Depth 8|Set-Content $BackupFile -Encoding UTF8
 Log 'Deep Gaming+Streaming profile enabled. Backup created.'
}
function RestoreDeepProfile{
 if(-not(Test-Path $BackupFile)){Log 'No Deep Profile backup found.';return};try{$b=Get-Content $BackupFile -Raw|ConvertFrom-Json;foreach($e in @($b.Registry)){RestoreReg $e};if($b.Power){& powercfg /S ([string]$b.Power)|Out-Null};Remove-Item $BackupFile -Force;Log 'Deep Profile restored.'}catch{Log ('Restore failed: '+$_.Exception.Message)}
}
function LoadGames{try{if(Test-Path $GamesFile){$script:LearnedGames=@(Get-Content $GamesFile -Raw|ConvertFrom-Json)}}catch{$script:LearnedGames=@()}}
function LearnForeground{
 [Windows.Forms.MessageBox]::Show('Press OK, switch to the game within 4 seconds.','D7 BLACKCORE')|Out-Null;$form.Hide();Start-Sleep 4;try{$sig='[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow(); [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);';if(-not('D7FG' -as [type])){Add-Type -MemberDefinition $sig -Name D7FG -Namespace Native};$h=[Native.D7FG]::GetForegroundWindow();[uint32]$id=0;[void][Native.D7FG]::GetWindowThreadProcessId($h,[ref]$id);$p=Get-Process -Id $id -ErrorAction Stop;$path=$p.Path;if($path){if(-not@($script:LearnedGames|Where-Object Path -eq $path).Count){$script:LearnedGames+=[pscustomobject]@{Name=$p.ProcessName;Path=$path;Added=(Get-Date).ToString('o')};$script:LearnedGames|ConvertTo-Json -Depth 5|Set-Content $GamesFile -Encoding UTF8;Log ('Learned game: '+$p.ProcessName)}}}catch{Log ('Learn game failed: '+$_.Exception.Message)};finally{$form.Show();$form.Activate()}
}
function SetPrioritySafe($p,$prio){try{if(-not$script:PriorityBackup.ContainsKey($p.Id)){$script:PriorityBackup[$p.Id]=$p.PriorityClass.ToString()};$p.PriorityClass=$prio}catch{}}
function DetectGame{foreach($g in @($script:LearnedGames)){try{$n=[IO.Path]::GetFileNameWithoutExtension($g.Path);$p=Get-Process -Name $n -ErrorAction SilentlyContinue|Select-Object -First 1;if($p -and $p.Path-eq$g.Path){return$p}}catch{}};return $null}
function SessionTick{
 $g=DetectGame;if($g -and -not$script:SessionActive){$script:SessionActive=$true;$script:GamePid=$g.Id;$script:GameName=$g.ProcessName;SetPrioritySafe $g ([Diagnostics.ProcessPriorityClass]::AboveNormal);try{$obs=Get-Process obs64 -ErrorAction SilentlyContinue|Select-Object -First 1;if($obs){SetPrioritySafe $obs ([Diagnostics.ProcessPriorityClass]::AboveNormal)}}catch{};try{(Get-Process -Id $PID).PriorityClass=[Diagnostics.ProcessPriorityClass]::BelowNormal}catch{};Log ('Adaptive session started: '+$script:GameName)}elseif(-not$g -and $script:SessionActive){foreach($id in @($script:PriorityBackup.Keys)){try{$p=Get-Process -Id ([int]$id)-ErrorAction Stop;$p.PriorityClass=[Diagnostics.ProcessPriorityClass][Enum]::Parse([Diagnostics.ProcessPriorityClass],$script:PriorityBackup[$id])}catch{}};$script:PriorityBackup=@{};$script:SessionActive=$false;$script:GamePid=0;Log 'Adaptive session ended.'}
}
function WingetReview{if(-not(Get-Command winget -ErrorAction SilentlyContinue)){[Windows.Forms.MessageBox]::Show('winget is not available.','D7 BLACKCORE')|Out-Null;return};Start-Process powershell.exe -ArgumentList '-NoExit','-Command','winget upgrade' -Verb RunAs}
function CheckUpdate{
 if($script:UpdateBusy){return};$script:UpdateBusy=$true;try{$m=Invoke-RestMethod ($ManifestUrl+'?t='+[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) -TimeoutSec 12;if([version]$m.version-gt[version]$Version){$ans=[Windows.Forms.MessageBox]::Show("D7 update $($m.version) is available. Install now?",'D7 Online Update',[Windows.Forms.MessageBoxButtons]::YesNo);if($ans-eq[Windows.Forms.DialogResult]::Yes){InstallUpdate $m}}else{[Windows.Forms.MessageBox]::Show("Latest version installed: $Version",'D7 Online Update')|Out-Null}}catch{Log ('Update check failed: '+$_.Exception.Message)}finally{$script:UpdateBusy=$false}}
function InstallUpdate($m){$d=Join-Path $UpdateRoot ([string]$m.version);Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue;New-Item $d -ItemType Directory -Force|Out-Null;$z=Join-Path $d 'pkg.zip';Invoke-WebRequest $m.packageUrl -OutFile $z -TimeoutSec 120;$h=(Get-FileHash $z -Algorithm SHA256).Hash.ToLower();if($h-ne([string]$m.sha256).ToLower()){throw'SHA256 mismatch'};Expand-Archive $z (Join-Path $d 'payload') -Force;$new=Get-ChildItem (Join-Path $d 'payload') -Filter D7_Gaming_Engine.exe -Recurse|Select-Object -First 1;$cur=[Environment]::GetCommandLineArgs()[0];$helper=Join-Path $d 'apply.ps1';$body="Wait-Process -Id $PID -ErrorAction SilentlyContinue`nCopy-Item -LiteralPath '$($new.FullName)' -Destination '$cur' -Force`nStart-Process -FilePath '$cur' -Verb RunAs";Set-Content $helper $body -Encoding UTF8;Start-Process powershell.exe -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File',$helper -WindowStyle Hidden;$form.Close()}

LoadGames
$form=New-Object Windows.Forms.Form;$form.Text='D7 BLACKCORE v2.0';$form.Size=New-Object Drawing.Size(1120,720);$form.StartPosition='CenterScreen';$form.BackColor=[Drawing.Color]::FromArgb(8,10,12);$form.ForeColor=[Drawing.Color]::White
$title=New-Object Windows.Forms.Label;$title.Text='D7 BLACKCORE - GAMING SYSTEM ORCHESTRATOR';$title.Font=New-Object Drawing.Font('Consolas',20,[Drawing.FontStyle]::Bold);$title.ForeColor=[Drawing.Color]::LawnGreen;$title.AutoSize=$true;$title.Location=New-Object Drawing.Point(30,25);$form.Controls.Add($title)
$sub=New-Object Windows.Forms.Label;$sub.Text='BLACKSCAN -> PLAN -> APPLY -> MEASURE -> LEARN -> ROLLBACK';$sub.Font=New-Object Drawing.Font('Consolas',10);$sub.ForeColor=[Drawing.Color]::DeepSkyBlue;$sub.AutoSize=$true;$sub.Location=New-Object Drawing.Point(33,70);$form.Controls.Add($sub)
function Btn($t,$x,$y,$w=180){$b=New-Object Windows.Forms.Button;$b.Text=$t;$b.Size=New-Object Drawing.Size($w,45);$b.Location=New-Object Drawing.Point($x,$y);$b.FlatStyle='Flat';$b.BackColor=[Drawing.Color]::FromArgb(25,25,25);$b.ForeColor=[Drawing.Color]::White;$form.Controls.Add($b);return$b}
$bScan=Btn 'BLACKSCAN FULL' 30 115 190;$bPlan=Btn 'SHOW D7 PLAN' 235 115 190;$bApply=Btn 'APPLY DEEP PROFILE' 440 115 190;$bRestore=Btn 'ROLLBACK' 645 115 150;$bLearn=Btn 'LEARN GAME' 810 115 130;$bUpdate=Btn 'ONLINE UPDATE' 950 115 130
$bTools=Btn 'TOOL HUB' 30 170 190;$bWinget=Btn 'REVIEW APP UPDATES' 235 170 190;$bFolder=Btn 'OPEN SCANS' 440 170 190
$stat=New-Object Windows.Forms.Label;$stat.Text='State: IDLE | waiting for BLACKSCAN';$stat.Font=New-Object Drawing.Font('Consolas',11,[Drawing.FontStyle]::Bold);$stat.ForeColor=[Drawing.Color]::Gold;$stat.AutoSize=$true;$stat.Location=New-Object Drawing.Point(30,235);$form.Controls.Add($stat)
$script:LogBox=New-Object Windows.Forms.TextBox;$script:LogBox.Multiline=$true;$script:LogBox.ReadOnly=$true;$script:LogBox.ScrollBars='Vertical';$script:LogBox.BackColor=[Drawing.Color]::Black;$script:LogBox.ForeColor=[Drawing.Color]::White;$script:LogBox.Font=New-Object Drawing.Font('Consolas',9);$script:LogBox.Location=New-Object Drawing.Point(30,275);$script:LogBox.Size=New-Object Drawing.Size(1040,360);$form.Controls.Add($script:LogBox)
$bScan.Add_Click({BlackScan;$stat.Text='State: BLACKSCAN COMPLETE';$stat.ForeColor=[Drawing.Color]::LawnGreen})
$bPlan.Add_Click({if(-not$script:LastPlan -and(Test-Path $LearnFile)){try{$x=Get-Content $LearnFile -Raw|ConvertFrom-Json;$script:LastPlan=$x.Plan}catch{}};if($script:LastPlan){[Windows.Forms.MessageBox]::Show(($script:LastPlan.Recommendations -join "`n`n"),('D7 Plan - Score '+$script:LastPlan.Score+'/100'))|Out-Null}else{[Windows.Forms.MessageBox]::Show('Run BLACKSCAN first.','D7 BLACKCORE')|Out-Null}})
$bApply.Add_Click({ApplyDeepProfile;$stat.Text='State: DEEP PROFILE ACTIVE';$stat.ForeColor=[Drawing.Color]::LawnGreen})
$bRestore.Add_Click({RestoreDeepProfile;$stat.Text='State: ROLLED BACK';$stat.ForeColor=[Drawing.Color]::Gold})
$bLearn.Add_Click({LearnForeground})
$bUpdate.Add_Click({CheckUpdate})
$bWinget.Add_Click({WingetReview})
$bFolder.Add_Click({Start-Process explorer.exe $ScanRoot})
$bTools.Add_Click({$t=ToolInventory;$s=$t|ForEach-Object{('{0,-28} {1,-10} {2}'-f$_.Name,$_.Installed,$_.Version)};[Windows.Forms.MessageBox]::Show(($s-join"`n"),'D7 Tool Hub')|Out-Null})
$timer=New-Object Windows.Forms.Timer;$timer.Interval=2500;$timer.Add_Tick({SessionTick;if($script:SessionActive){$stat.Text='State: GAMING | '+$script:GameName;$stat.ForeColor=[Drawing.Color]::LawnGreen}});$timer.Start()
Log ('D7 BLACKCORE v'+$Version+' started.');Log 'Policy: reversible changes only. No HPET/BCD/timer hacks, no RealTime priority, no Defender/pagefile disabling.'
[void]$form.ShowDialog()
