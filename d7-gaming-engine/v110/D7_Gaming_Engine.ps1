# D7 Gaming Engine v1.1.0 - Gaming + Streaming Edition
# Lightweight, reversible, gaming/streaming-focused Windows profile.
# No HPET/BCD/timer hacks, no RAM cleaners, no RealTime priority, no anti-cheat/game-memory access.

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class D7Native {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public class MEMORYSTATUSEX {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    public struct FILETIME { public uint dwLowDateTime; public uint dwHighDateTime; }
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);
    static ulong Ft(FILETIME t) { return ((ulong)t.dwHighDateTime << 32) | t.dwLowDateTime; }
    static ulong lastIdle, lastKernel, lastUser;
    static bool cpuInit = false;
    public static double CpuUsage() {
        FILETIME i,k,u; if(!GetSystemTimes(out i,out k,out u)) return 0;
        ulong idle=Ft(i), kernel=Ft(k), user=Ft(u);
        if(!cpuInit){ lastIdle=idle; lastKernel=kernel; lastUser=user; cpuInit=true; return 0; }
        ulong idleD=idle-lastIdle, kernelD=kernel-lastKernel, userD=user-lastUser;
        lastIdle=idle; lastKernel=kernel; lastUser=user;
        ulong total=kernelD+userD; if(total==0) return 0;
        double busy=(double)(total-idleD)/total*100.0;
        if(busy<0) busy=0; if(busy>100) busy=100; return busy;
    }
}
"@

[System.Windows.Forms.Application]::EnableVisualStyles()

$Version='1.1.0'
$ManifestUrl='https://raw.githubusercontent.com/Emperor0/em/main/d7-gaming-engine/stable/latest.json'
$AppRoot=Join-Path $env:ProgramData 'D7 Gaming Engine'
$LogDir=Join-Path $AppRoot 'GamingLogs'
$StateDir=Join-Path $AppRoot 'AdaptiveState'
$BackupFile=Join-Path $StateDir 'gaming-streaming-profile-backup.json'
$GamesFile=Join-Path $AppRoot 'learned-games.json'
$UpdateDir=Join-Path $AppRoot 'Updates'
@($AppRoot,$LogDir,$StateDir,$UpdateDir)|ForEach-Object{New-Item -ItemType Directory -Path $_ -Force|Out-Null}
$LogPath=Join-Path $LogDir ("D7_v110_{0}.log" -f (Get-Date -Format 'yyyyMMdd_HHmmss'))

$script:Exiting=$false
$script:ProfileEnabled=$false
$script:SessionActive=$false
$script:GamePid=0
$script:GameName=''
$script:GamePath=''
$script:LearnedGames=@()
$script:LastGpuPoll=[datetime]::MinValue
$script:Gpu=[pscustomobject]@{Available=$false;Util=0;Temp=0;VramUsed=0;VramTotal=0;Clock=0;Power=0}
$script:LastDetect=[datetime]::MinValue
$script:UpdateBusy=$false
$script:StartupUpdateTimer=$null
$script:OriginalSelfPriority=$null
$script:SessionPriorityBackup=@{}
$script:PowerBackup=$null

$script:Excluded=@('explorer','dwm','ShellExperienceHost','StartMenuExperienceHost','SearchHost','SearchApp','ApplicationFrameHost','SystemSettings','Taskmgr','powershell','pwsh','cmd','conhost','Discord','obs64','steam','steamwebhelper','EpicGamesLauncher','Battle.net','Agent','EADesktop','EALauncher','UbisoftConnect','upc','GalaxyClient','NVIDIA App','NVIDIA Share','NVIDIA Overlay','nvcontainer','ChatGPT','audiodg','csrss','winlogon','services','lsass')
$script:KnownBackground=@('OneDrive','OneDriveStandaloneUpdater','AdobeARM','CCXProcess','Creative Cloud','GoogleUpdate','MicrosoftEdgeUpdate','msedgeupdate','Update','updater')
$script:GamePathPatterns=@('\\steamapps\\common\\','\\SteamLibrary\\','\\XboxGames\\','\\Epic Games\\','\\EA Games\\','\\Origin Games\\','\\Ubisoft\\','\\GOG Galaxy\\Games\\','\\Battle.net\\','\\Call of Duty\\','\\Games\\')

function Write-Log([string]$Text){
    $line="[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'),$Text
    try{Add-Content -Path $LogPath -Value $line -Encoding UTF8}catch{}
    if($script:LogBox -and -not $script:LogBox.IsDisposed){$script:LogBox.AppendText($line+[Environment]::NewLine);$script:LogBox.SelectionStart=$script:LogBox.TextLength;$script:LogBox.ScrollToCaret()}
}

function Load-State{
    try{if(Test-Path $GamesFile){$script:LearnedGames=@(Get-Content $GamesFile -Raw -Encoding UTF8|ConvertFrom-Json)}}catch{$script:LearnedGames=@()}
    $script:ProfileEnabled=Test-Path $BackupFile
}
function Save-LearnedGames{try{$script:LearnedGames|ConvertTo-Json -Depth 5|Set-Content $GamesFile -Encoding UTF8}catch{}}

function Get-CurrentExePath{try{$p=[Environment]::GetCommandLineArgs()[0];if($p-and(Test-Path $p)){return(Resolve-Path $p).Path}}catch{};return $null}
function Compare-Version([string]$A,[string]$B){try{return([version]$A).CompareTo([version]$B)}catch{return 0}}

function Get-Memory{
    try{$m=New-Object D7Native+MEMORYSTATUSEX;if([D7Native]::GlobalMemoryStatusEx($m)){return[pscustomobject]@{Total=[math]::Round($m.ullTotalPhys/1GB,2);Free=[math]::Round($m.ullAvailPhys/1GB,2);Load=[int]$m.dwMemoryLoad}}}catch{}
    return[pscustomobject]@{Total=0;Free=0;Load=0}
}
function Get-Cpu{try{return[math]::Round([D7Native]::CpuUsage(),1)}catch{return 0}}
function Get-NvidiaTelemetry{
    $o=[ordered]@{Available=$false;Util=0;Temp=0;VramUsed=0;VramTotal=0;Clock=0;Power=0}
    try{
        $line=& nvidia-smi.exe --query-gpu=utilization.gpu,temperature.gpu,memory.used,memory.total,clocks.current.graphics,power.draw --format=csv,noheader,nounits 2>$null|Select-Object -First 1
        if($LASTEXITCODE-eq 0-and$line){$a=$line -split ','|ForEach-Object{$_.Trim()};$o.Available=$true;$o.Util=[double]$a[0];$o.Temp=[double]$a[1];$o.VramUsed=[double]$a[2];$o.VramTotal=[double]$a[3];$o.Clock=[double]$a[4];$o.Power=[double]$a[5]}
    }catch{}
    return[pscustomobject]$o
}

function Get-ForegroundProcess{
    try{$h=[D7Native]::GetForegroundWindow();if($h-eq[IntPtr]::Zero){return$null};[uint32]$id=0;[void][D7Native]::GetWindowThreadProcessId($h,[ref]$id);if($id-eq 0-or$id-eq$PID){return$null};return Get-Process -Id $id -ErrorAction Stop}catch{return$null}
}
function Get-ProcessPath($P){try{return$P.Path}catch{};return$null}
function Test-Excluded($P){if(-not$P){return$true};foreach($n in $script:Excluded){if($P.ProcessName-ieq$n){return$true}};return$false}
function Test-LikelyGame($P){
    if(-not$P-or(Test-Excluded $P)){return$false};$path=Get-ProcessPath $P;if([string]::IsNullOrWhiteSpace($path)){return$false}
    if(@($script:LearnedGames|Where-Object{$_.Path-ieq$path}).Count){return$true}
    foreach($pat in $script:GamePathPatterns){if($path-match[regex]::Escape($pat)-and$P.ProcessName-notmatch'(?i)(launcher|crash|report|helper|updater|setup|install)'){return$true}}
    return$false
}
function Get-DetectedGame{
    if($script:SessionActive){$p=Get-Process -Id $script:GamePid -ErrorAction SilentlyContinue;if($p){return$p}}
    $fg=Get-ForegroundProcess;if($fg-and(Test-LikelyGame $fg)){return$fg}
    foreach($g in @($script:LearnedGames)){try{$n=[IO.Path]::GetFileNameWithoutExtension([string]$g.Path);$p=Get-Process -Name $n -ErrorAction SilentlyContinue|Select-Object -First 1;if($p-and(Get-ProcessPath $p)-ieq[string]$g.Path){return$p}}catch{}}
    return$null
}

function Get-RegValue([string]$Path,[string]$Name){
    try{$v=Get-ItemProperty -Path $Path -Name $Name -ErrorAction Stop;return[pscustomobject]@{Exists=$true;Value=$v.$Name}}catch{return[pscustomobject]@{Exists=$false;Value=$null}}
}
function Set-RegDword([string]$Path,[string]$Name,[int]$Value){New-Item $Path -Force|Out-Null;New-ItemProperty $Path -Name $Name -PropertyType DWord -Value $Value -Force|Out-Null}
function Set-RegString([string]$Path,[string]$Name,[string]$Value){New-Item $Path -Force|Out-Null;New-ItemProperty $Path -Name $Name -PropertyType String -Value $Value -Force|Out-Null}
function Restore-RegEntry($E){
    try{if($E.Exists){if($E.Type-eq'DWord'){Set-RegDword $E.Path $E.Name ([int]$E.Value)}else{Set-RegString $E.Path $E.Name ([string]$E.Value)}}else{Remove-ItemProperty -Path $E.Path -Name $E.Name -Force -ErrorAction SilentlyContinue}}catch{}
}
function Backup-Reg([System.Collections.ArrayList]$List,[string]$Path,[string]$Name,[string]$Type){$v=Get-RegValue $Path $Name;[void]$List.Add([pscustomobject]@{Path=$Path;Name=$Name;Type=$Type;Exists=$v.Exists;Value=$v.Value})}
function Get-ActivePowerScheme{try{$t=(& powercfg.exe /GETACTIVESCHEME 2>&1|Out-String);if($t-match'([0-9a-fA-F-]{36})'){return$matches[1]}}catch{};return$null}
function Set-HighPerformance{try{& powercfg.exe /S '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c' 2>$null|Out-Null;return($LASTEXITCODE-eq 0)}catch{return$false}}

function Apply-GamingStreamingProfile{
    if($script:ProfileEnabled){Write-Log 'Gaming+Streaming profile already enabled.';return}
    $regs=New-Object System.Collections.ArrayList
    $targets=@(
        @('HKCU:\Software\Microsoft\GameBar','AutoGameModeEnabled','DWord',1),
        @('HKCU:\Software\Microsoft\GameBar','AllowAutoGameMode','DWord',1),
        @('HKCU:\System\GameConfigStore','GameDVR_Enabled','DWord',0),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR','AppCaptureEnabled','DWord',0),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects','VisualFXSetting','DWord',2),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced','TaskbarAnimations','DWord',0),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications','GlobalUserDisabled','DWord',1),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge','BackgroundModeEnabled','DWord',0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge','StartupBoostEnabled','DWord',0)
    )
    foreach($t in $targets){Backup-Reg $regs $t[0] $t[1] $t[2];if($t[2]-eq'DWord'){Set-RegDword $t[0] $t[1] ([int]$t[3])}else{Set-RegString $t[0] $t[1] ([string]$t[3])}}
    Backup-Reg $regs 'HKCU:\Control Panel\Desktop\WindowMetrics' 'MinAnimate' 'String';Set-RegString 'HKCU:\Control Panel\Desktop\WindowMetrics' 'MinAnimate' '0'
    $power=Get-ActivePowerScheme
    if(Set-HighPerformance){Write-Log 'Power plan: High Performance enabled.'}
    $backup=[ordered]@{Version=$Version;Applied=(Get-Date).ToString('o');PowerScheme=$power;Registry=@($regs)}
    $backup|ConvertTo-Json -Depth 8|Set-Content $BackupFile -Encoding UTF8
    $script:ProfileEnabled=$true
    Write-Log 'Gaming + Streaming dedicated profile ENABLED.'
    Write-Log 'Applied: Game Mode ON, background capture OFF, visual effects reduced, background Store apps reduced, Edge background/startup boost OFF.'
    Write-Log 'Not touched: Defender, pagefile, SysMain, HPET/BCD/timers, VBS/HVCI, network registry hacks.'
    if($script:BtnProfile){$script:BtnProfile.Text='وضع ألعاب + بث: مفعّل';$script:BtnProfile.BackColor=[Drawing.Color]::DarkGreen}
}
function Restore-GamingStreamingProfile{
    if(-not(Test-Path $BackupFile)){Write-Log 'No profile backup found.';return}
    try{$b=Get-Content $BackupFile -Raw -Encoding UTF8|ConvertFrom-Json;foreach($e in @($b.Registry)){Restore-RegEntry $e};if($b.PowerScheme){& powercfg.exe /S ([string]$b.PowerScheme) 2>$null|Out-Null};Remove-Item $BackupFile -Force;$script:ProfileEnabled=$false;Write-Log 'Gaming + Streaming profile RESTORED.';if($script:BtnProfile){$script:BtnProfile.Text='تفعيل وضع ألعاب + بث';$script:BtnProfile.BackColor=[Drawing.Color]::FromArgb(30,30,30)}}catch{Write-Log("Restore failed: {0}"-f$_.Exception.Message)}
}

function Backup-SetPriority($P,[System.Diagnostics.ProcessPriorityClass]$Priority){
    try{if(-not$script:SessionPriorityBackup.ContainsKey($P.Id)){$script:SessionPriorityBackup[$P.Id]=$P.PriorityClass.ToString()};if($P.PriorityClass-ne$Priority){$P.PriorityClass=$Priority;Write-Log("Priority: {0} -> {1}"-f$P.ProcessName,$Priority)}}catch{}
}
function Restore-SessionPriorities{foreach($id in @($script:SessionPriorityBackup.Keys)){try{$p=Get-Process -Id ([int]$id)-ErrorAction Stop;$old=[System.Diagnostics.ProcessPriorityClass][Enum]::Parse([System.Diagnostics.ProcessPriorityClass],[string]$script:SessionPriorityBackup[$id]);$p.PriorityClass=$old}catch{}};$script:SessionPriorityBackup=@{}}
function Apply-BackgroundRelief{
    foreach($name in $script:KnownBackground){try{Get-Process -Name $name -ErrorAction SilentlyContinue|ForEach-Object{Backup-SetPriority $_ ([System.Diagnostics.ProcessPriorityClass]::BelowNormal)}}catch{}}
}
function Start-GameSession($Game){
    if($script:SessionActive-or-not$Game){return};$script:SessionActive=$true;$script:GamePid=$Game.Id;$script:GameName=$Game.ProcessName;$script:GamePath=Get-ProcessPath $Game
    Write-Log("=== GAME SESSION: {0} PID={1} ==="-f$script:GameName,$script:GamePid)
    Backup-SetPriority $Game ([System.Diagnostics.ProcessPriorityClass]::AboveNormal)
    try{$obs=Get-Process obs64 -ErrorAction SilentlyContinue|Select-Object -First 1;if($obs){Backup-SetPriority $obs ([System.Diagnostics.ProcessPriorityClass]::AboveNormal);Write-Log 'OBS detected: protected at AboveNormal priority.'}}catch{}
    Apply-BackgroundRelief
    try{$self=Get-Process -Id $PID;$script:OriginalSelfPriority=$self.PriorityClass.ToString();$self.PriorityClass=[System.Diagnostics.ProcessPriorityClass]::BelowNormal}catch{}
    if($script:GamePath){try{Set-RegString 'HKCU:\Software\Microsoft\DirectX\UserGpuPreferences' $script:GamePath 'GpuPreference=2;'}catch{}}
    if($script:StatusLabel){$script:StatusLabel.Text="Gaming session: $($script:GameName)";$script:StatusLabel.ForeColor=[Drawing.Color]::LawnGreen}
}
function Stop-GameSession{
    if(-not$script:SessionActive){return};Write-Log("=== GAME END: {0} ==="-f$script:GameName);Restore-SessionPriorities
    try{if($script:OriginalSelfPriority){(Get-Process -Id $PID).PriorityClass=[System.Diagnostics.ProcessPriorityClass][Enum]::Parse([System.Diagnostics.ProcessPriorityClass],$script:OriginalSelfPriority)}}catch{}
    $script:OriginalSelfPriority=$null;$script:SessionActive=$false;$script:GamePid=0;$script:GameName='';$script:GamePath=''
    if($script:StatusLabel){$script:StatusLabel.Text='D7 خفيف بالخلفية — افتح لعبة';$script:StatusLabel.ForeColor=[Drawing.Color]::DeepSkyBlue}
}

function Learn-CurrentGame{
    Write-Log 'Learn game: D7 will hide for 4 seconds. Return to the game.';$form.Hide();Start-Sleep 4;$p=Get-ForegroundProcess;$form.Show();$form.Activate();if(-not$p-or(Test-Excluded $p)){Write-Log 'No valid game captured.';return};$path=Get-ProcessPath $p;if([string]::IsNullOrWhiteSpace($path)){return};if(-not@($script:LearnedGames|Where-Object{$_.Path-ieq$path}).Count){$script:LearnedGames+=[pscustomobject]@{Name=$p.ProcessName;Path=$path;Added=(Get-Date).ToString('o')};Save-LearnedGames;Write-Log("Learned game: {0}"-f$p.ProcessName)};if(-not$script:SessionActive){Start-GameSession $p}
}

function Deep-Scan{
    Write-Log '--- D7 DEEP SCAN ---'
    try{$os=Get-CimInstance Win32_OperatingSystem;Write-Log("Windows build: {0} | Free system drive: {1:N1}GB"-f$os.BuildNumber,((Get-PSDrive $env:SystemDrive.TrimEnd(':')).Free/1GB))}catch{}
    try{$cpu=Get-CimInstance Win32_Processor|Select-Object -First 1;Write-Log("CPU: {0} | {1}C/{2}T"-f$cpu.Name.Trim(),$cpu.NumberOfCores,$cpu.NumberOfLogicalProcessors)}catch{}
    $m=Get-Memory;Write-Log("RAM: {0}GB total / {1}GB free / {2}% used"-f$m.Total,$m.Free,$m.Load)
    $g=Get-NvidiaTelemetry;if($g.Available){Write-Log("GPU: NVIDIA | Util={0}% Temp={1}C VRAM={2}/{3}MB Clock={4}MHz Power={5}W"-f$g.Util,$g.Temp,$g.VramUsed,$g.VramTotal,$g.Clock,$g.Power)}else{Write-Log 'GPU telemetry: nvidia-smi unavailable.'}
    try{$runCount=0;foreach($p in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Run','HKLM:\Software\Microsoft\Windows\CurrentVersion\Run')){if(Test-Path $p){$o=Get-ItemProperty $p;$runCount+=@($o.PSObject.Properties|Where-Object{$_.Name-notmatch'^PS'}).Count}};Write-Log("Startup Run entries: {0}"-f$runCount)}catch{}
    try{$dg=Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard -ErrorAction Stop;Write-Log("VBS status: {0} | Security services running: {1}"-f($dg.VirtualizationBasedSecurityStatus),(@($dg.SecurityServicesRunning)-join','))}catch{Write-Log 'VBS status: unavailable on this system.'}
    Write-Log 'Deep scan complete. Heavy WMI/CIM calls are used ONLY here, never in the live loop.'
}

function Check-OnlineUpdate([bool]$Interactive=$false){
    if($script:UpdateBusy){return};$script:UpdateBusy=$true
    try{
        [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
        $m=Invoke-RestMethod -Uri ($ManifestUrl+'?t='+[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) -UseBasicParsing -TimeoutSec 10
        if(-not$m.version-or-not$m.packageUrl-or-not$m.sha256){throw'manifest incomplete'}
        if((Compare-Version ([string]$m.version) $Version)-le 0){if($Interactive){[Windows.Forms.MessageBox]::Show("أنت على أحدث إصدار: $Version",'D7 Online Update')|Out-Null};return}
        $ans=[Windows.Forms.DialogResult]::No
        if($Interactive-or$form.Visible){$ans=[Windows.Forms.MessageBox]::Show("تحديث D7 $($m.version) متاح.\n\n$($m.changelogAr)\n\nتثبيت الآن؟",'D7 Online Update',[Windows.Forms.MessageBoxButtons]::YesNo,[Windows.Forms.MessageBoxIcon]::Information)}
        if($ans-eq[Windows.Forms.DialogResult]::Yes){Install-OnlineUpdate $m}
    }catch{Write-Log("Update check failed: {0}"-f$_.Exception.Message);if($Interactive){[Windows.Forms.MessageBox]::Show('تعذر فحص التحديث الآن.','D7 Online Update')|Out-Null}}
    finally{$script:UpdateBusy=$false}
}
function Install-OnlineUpdate($Manifest){
    $ver=[string]$Manifest.version;$dir=Join-Path $UpdateDir $ver;Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue;New-Item $dir -ItemType Directory -Force|Out-Null
    $zip=Join-Path $dir 'package.zip';Invoke-WebRequest -Uri ([string]$Manifest.packageUrl) -OutFile $zip -UseBasicParsing -TimeoutSec 120
    $hash=(Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant();if($hash-ne([string]$Manifest.sha256).ToLowerInvariant()){throw'SHA256 mismatch'}
    $payload=Join-Path $dir 'payload';Expand-Archive $zip $payload -Force;$newExe=Get-ChildItem $payload -Recurse -File -Filter 'D7_Gaming_Engine.exe'|Select-Object -First 1;if(-not$newExe){throw'EXE missing'}
    $current=Get-CurrentExePath;if(-not$current){throw'current exe unavailable'};$helper=Join-Path $dir 'apply-update.ps1';$pidNow=$PID
    $code=@"
`$ErrorActionPreference='Stop'
try{Wait-Process -Id $pidNow -Timeout 30 -ErrorAction SilentlyContinue}catch{}
Start-Sleep -Milliseconds 800
Copy-Item -LiteralPath '$($newExe.FullName.Replace("'","''"))' -Destination '$($current.Replace("'","''"))' -Force
Start-Process -FilePath '$($current.Replace("'","''"))'
"@
    Set-Content $helper $code -Encoding UTF8;Write-Log("Update verified: $ver. Restarting.");$script:Exiting=$true;Stop-GameSession;Start-Process powershell.exe -WindowStyle Hidden -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$helper+'"'));$tray.Visible=$false;$form.Close()
}

function Enable-Startup{try{$exe=Get-CurrentExePath;if($exe){$r='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run';New-Item $r -Force|Out-Null;New-ItemProperty $r -Name 'D7GamingEngine' -PropertyType String -Value ('"{0}" --background'-f$exe) -Force|Out-Null;return$true}}catch{};return$false}
function Disable-Startup{try{Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'D7GamingEngine' -Force -ErrorAction SilentlyContinue;return$true}catch{return$false}}
function Test-StartupEnabled{try{return-not[string]::IsNullOrWhiteSpace((Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'D7GamingEngine' -ErrorAction Stop).D7GamingEngine)}catch{return$false}}

Load-State
$form=New-Object Windows.Forms.Form;$form.Text="D7 Gaming Engine v$Version — Gaming + Streaming";$form.Size=New-Object Drawing.Size(1120,760);$form.StartPosition='CenterScreen';$form.BackColor=[Drawing.Color]::FromArgb(10,14,18);$form.ForeColor=[Drawing.Color]::White;$form.RightToLeft='Yes';$form.RightToLeftLayout=$true;$form.Font=New-Object Drawing.Font('Segoe UI',10)
$title=New-Object Windows.Forms.Label;$title.Text='D7 GAMING + STREAMING ENGINE';$title.Font=New-Object Drawing.Font('Segoe UI Semibold',22,[Drawing.FontStyle]::Bold);$title.ForeColor=[Drawing.Color]::Chartreuse;$title.AutoSize=$true;$title.Location=New-Object Drawing.Point(585,20);$form.Controls.Add($title)
$status=New-Object Windows.Forms.Label;$status.Text='D7 خفيف بالخلفية — افتح لعبة';$status.AutoSize=$true;$status.Location=New-Object Drawing.Point(725,70);$status.ForeColor=[Drawing.Color]::DeepSkyBlue;$form.Controls.Add($status);$script:StatusLabel=$status
$metrics=New-Object Windows.Forms.Label;$metrics.Text='CPU -- | RAM -- | GPU -- | TEMP -- | VRAM --';$metrics.Font=New-Object Drawing.Font('Consolas',11,[Drawing.FontStyle]::Bold);$metrics.AutoSize=$false;$metrics.Size=New-Object Drawing.Size(930,30);$metrics.Location=New-Object Drawing.Point(105,112);$metrics.ForeColor=[Drawing.Color]::Gold;$form.Controls.Add($metrics);$script:MetricsLabel=$metrics
$hint=New-Object Windows.Forms.Label;$hint.Text='مخصص للألعاب والبث: يقلل الحمل الخلفي ويحافظ على OBS واللعبة. لا يستخدم تويكات قديمة خطرة.';$hint.AutoSize=$false;$hint.Size=New-Object Drawing.Size(930,40);$hint.Location=New-Object Drawing.Point(105,150);$hint.ForeColor=[Drawing.Color]::Silver;$form.Controls.Add($hint)

$profile=New-Object Windows.Forms.Button;$profile.Size=New-Object Drawing.Size(210,46);$profile.Location=New-Object Drawing.Point(825,205);$profile.Text=if($script:ProfileEnabled){'وضع ألعاب + بث: مفعّل'}else{'تفعيل وضع ألعاب + بث'};if($script:ProfileEnabled){$profile.BackColor=[Drawing.Color]::DarkGreen};$form.Controls.Add($profile);$script:BtnProfile=$profile
$restore=New-Object Windows.Forms.Button;$restore.Size=New-Object Drawing.Size(170,46);$restore.Location=New-Object Drawing.Point(635,205);$restore.Text='استرجاع كل التعديلات';$form.Controls.Add($restore)
$scan=New-Object Windows.Forms.Button;$scan.Size=New-Object Drawing.Size(170,46);$scan.Location=New-Object Drawing.Point(445,205);$scan.Text='فحص عميق';$form.Controls.Add($scan)
$learn=New-Object Windows.Forms.Button;$learn.Size=New-Object Drawing.Size(170,46);$learn.Location=New-Object Drawing.Point(255,205);$learn.Text='تعلم اللعبة الحالية';$form.Controls.Add($learn)
$update=New-Object Windows.Forms.Button;$update.Size=New-Object Drawing.Size(130,46);$update.Location=New-Object Drawing.Point(105,205);$update.Text='تحديث أونلاين';$form.Controls.Add($update)
$chk=New-Object Windows.Forms.CheckBox;$chk.Text='تشغيل D7 تلقائيًا مع Windows';$chk.AutoSize=$true;$chk.Location=New-Object Drawing.Point(780,272);$chk.Checked=Test-StartupEnabled;$form.Controls.Add($chk)
$policy=New-Object Windows.Forms.Label;$policy.Text='Live loop خفيف: CPU/RAM من Win32 مباشرة، GPU كل 5 ثوانٍ أثناء اللعب فقط. WMI/CIM لا يعمل بالخلفية.';$policy.AutoSize=$false;$policy.Size=New-Object Drawing.Size(930,38);$policy.Location=New-Object Drawing.Point(105,308);$policy.ForeColor=[Drawing.Color]::DarkGray;$form.Controls.Add($policy)
$log=New-Object Windows.Forms.TextBox;$log.Multiline=$true;$log.ReadOnly=$true;$log.ScrollBars='Vertical';$log.BackColor=[Drawing.Color]::FromArgb(5,9,13);$log.ForeColor=[Drawing.Color]::Gainsboro;$log.Size=New-Object Drawing.Size(930,330);$log.Location=New-Object Drawing.Point(105,350);$log.RightToLeft='No';$form.Controls.Add($log);$script:LogBox=$log

$profile.Add_Click({Apply-GamingStreamingProfile})
$restore.Add_Click({Stop-GameSession;Restore-GamingStreamingProfile})
$scan.Add_Click({Deep-Scan})
$learn.Add_Click({Learn-CurrentGame})
$update.Add_Click({Check-OnlineUpdate $true})
$chk.Add_CheckedChanged({if($chk.Checked){[void](Enable-Startup)}else{[void](Disable-Startup)}})

$timer=New-Object Windows.Forms.Timer;$timer.Interval=1000;$timer.Add_Tick({
    $cpu=Get-Cpu;$mem=Get-Memory
    if(((Get-Date)-$script:LastDetect).TotalSeconds-ge 2){$script:LastDetect=Get-Date;if($script:SessionActive){$g=Get-Process -Id $script:GamePid -ErrorAction SilentlyContinue;if(-not$g){Stop-GameSession}}else{$g=Get-DetectedGame;if($g){Start-GameSession $g}}}
    if($script:SessionActive-and((Get-Date)-$script:LastGpuPoll).TotalSeconds-ge 5){$script:LastGpuPoll=Get-Date;$script:Gpu=Get-NvidiaTelemetry}
    $g=$script:Gpu
    if($script:MetricsLabel){$script:MetricsLabel.Text=("CPU {0,5}% | RAM {1,3}% ({2}GB free) | GPU {3,5}% | TEMP {4,3}C | VRAM {5}/{6}MB"-f$cpu,$mem.Load,$mem.Free,$g.Util,$g.Temp,$g.VramUsed,$g.VramTotal)}
});$timer.Start()

$tray=New-Object Windows.Forms.NotifyIcon;$tray.Text="D7 Gaming Engine v$Version";$tray.Icon=[Drawing.SystemIcons]::Application;$tray.Visible=$true;$menu=New-Object Windows.Forms.ContextMenuStrip;$show=$menu.Items.Add('فتح D7');$mode=$menu.Items.Add('تفعيل وضع ألعاب + بث');$upd=$menu.Items.Add('فحص تحديث أونلاين');$exit=$menu.Items.Add('خروج');$tray.ContextMenuStrip=$menu;$show.Add_Click({$form.Show();$form.WindowState='Normal';$form.Activate()});$mode.Add_Click({Apply-GamingStreamingProfile});$upd.Add_Click({Check-OnlineUpdate $true});$exit.Add_Click({$script:Exiting=$true;Stop-GameSession;$timer.Stop();$tray.Visible=$false;$form.Close()});$tray.Add_DoubleClick({$form.Show();$form.WindowState='Normal';$form.Activate()})
$form.Add_FormClosing({param($sender,$e);if(-not$script:Exiting){$e.Cancel=$true;$form.Hide();$tray.ShowBalloonTip(1200,'D7 Gaming Engine','D7 مستمر بالخلفية بوضع خفيف.','Info')}})
$form.Add_Shown({
    Write-Log("D7 Gaming Engine v$Version started.");Write-Log 'Engine: lightweight live loop + dedicated gaming/streaming profile.';Write-Log 'Safety: reversible profile; no HPET/BCD/timer/RAM-cleaner hacks.'
    if(-not(Test-StartupEnabled)){if(Enable-Startup){$chk.Checked=$true}}
    [void](Get-Cpu)
    if([Environment]::GetCommandLineArgs()-contains'--background'){$form.Hide()}
    $script:StartupUpdateTimer=New-Object Windows.Forms.Timer;$script:StartupUpdateTimer.Interval=4500;$script:StartupUpdateTimer.Add_Tick({if($script:StartupUpdateTimer){$script:StartupUpdateTimer.Stop()};Check-OnlineUpdate $false});$script:StartupUpdateTimer.Start()
})
[void]$form.ShowDialog();if($script:SessionActive){Stop-GameSession};$tray.Visible=$false
