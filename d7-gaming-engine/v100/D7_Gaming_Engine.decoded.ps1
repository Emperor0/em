# D7 Gaming Engine v1.0.0 - Adaptive Performance Engine
# Safe adaptive gaming agent for Windows 10/11.
# Policy: reversible session changes only. No BCD/HPET/timer forcing, no RAM purge, no anti-cheat/game-memory access.

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class D7Win32 {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
"@

[System.Windows.Forms.Application]::EnableVisualStyles()

$Version = '1.0.0'
$ManifestUrl = 'https://raw.githubusercontent.com/Emperor0/em/main/d7-gaming-engine/stable/latest.json'
$AppRoot = Join-Path $env:ProgramData 'D7 Gaming Engine'
$LogDir = Join-Path $AppRoot 'GamingLogs'
$StateDir = Join-Path $AppRoot 'AdaptiveState'
$GamesFile = Join-Path $AppRoot 'learned-games.json'
$ProfilesFile = Join-Path $StateDir 'game-profiles.json'
$UpdateDir = Join-Path $AppRoot 'Updates'
@($AppRoot,$LogDir,$StateDir,$UpdateDir) | ForEach-Object { New-Item -ItemType Directory -Path $_ -Force | Out-Null }
$LogPath = Join-Path $LogDir ("D7_Adaptive_{0}.log" -f (Get-Date -Format 'yyyyMMdd_HHmmss'))

$script:SessionActive=$false
$script:GamePid=0
$script:GameName=''
$script:GamePath=''
$script:OriginalScheme=$null
$script:PriorityBackup=@{}
$script:GameModeBackup=@{}
$script:LearnedGames=@()
$script:Profiles=@{}
$script:SensorHistory=New-Object System.Collections.ArrayList
$script:LastGameCpuTime=$null
$script:LastGameCpuStamp=$null
$script:LastBgCpu=@{}
$script:LastBgStamp=Get-Date
$script:LastAdaptiveAction=[datetime]::MinValue
$script:LastProfileSave=[datetime]::MinValue
$script:Exiting=$false
$script:LogicalCpu=[Environment]::ProcessorCount
$script:LastState='IDLE'
$script:LastConfidence=0
$script:CurrentTelemetry=$null

$script:ExcludedNames=@(
'explorer','dwm','ShellExperienceHost','StartMenuExperienceHost','SearchApp','SearchHost','ApplicationFrameHost',
'SystemSettings','Taskmgr','powershell','pwsh','cmd','conhost','Discord','obs64','steam','steamwebhelper',
'EpicGamesLauncher','Battle.net','Agent','EADesktop','EALauncher','UbisoftConnect','upc','GalaxyClient',
'NVIDIA App','NVIDIA Share','NVIDIA Overlay','nvcontainer','ChatGPT','devenv','Code','audiodg','csrss','winlogon','services','lsass'
)
$script:BackgroundCandidates=@('OneDrive','OneDriveStandaloneUpdater','CCXProcess','AdobeARM','Creative Cloud','fdm','Urban Vpn Updater','GoogleUpdate','MicrosoftEdgeUpdate')
$script:GamePathPatterns=@('\\steamapps\\common\\','\\SteamLibrary\\','\\XboxGames\\','\\Epic Games\\','\\EA Games\\','\\Origin Games\\','\\Ubisoft\\','\\GOG Galaxy\\Games\\','\\Battle.net\\','\\Call of Duty\\','\\Games\\')

function Write-Log([string]$Text){
    $line="[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'),$Text
    try{Add-Content -Path $LogPath -Value $line -Encoding UTF8}catch{}
    if($script:LogBox -and -not $script:LogBox.IsDisposed){
        $script:LogBox.AppendText($line+[Environment]::NewLine)
        $script:LogBox.SelectionStart=$script:LogBox.TextLength
        $script:LogBox.ScrollToCaret()
    }
}

function Load-JsonFile($Path,$Default){
    try{ if(Test-Path $Path){ return (Get-Content $Path -Raw -Encoding UTF8 | ConvertFrom-Json) } }catch{}
    return $Default
}
function Save-JsonFile($Path,$Object){
    try{ $Object | ConvertTo-Json -Depth 8 | Set-Content $Path -Encoding UTF8; return $true }catch{return $false}
}
function Load-State{
    $script:LearnedGames=@(Load-JsonFile $GamesFile @())
    $p=Load-JsonFile $ProfilesFile $null
    $script:Profiles=@{}
    if($p){ foreach($prop in $p.PSObject.Properties){ $script:Profiles[$prop.Name]=$prop.Value } }
}
function Save-Profiles{
    $o=[ordered]@{}
    foreach($k in $script:Profiles.Keys){$o[$k]=$script:Profiles[$k]}
    [void](Save-JsonFile $ProfilesFile $o)
    $script:LastProfileSave=Get-Date
}
function Add-LearnedGame([string]$Path,[string]$Name){
    if([string]::IsNullOrWhiteSpace($Path) -or -not(Test-Path $Path)){return $false}
    if(-not @($script:LearnedGames|Where-Object{$_.Path -ieq $Path}).Count){
        $script:LearnedGames += [pscustomobject]@{Name=$Name;Path=$Path;Added=(Get-Date).ToString('o')}
        [void](Save-JsonFile $GamesFile $script:LearnedGames)
        Write-Log("تعلمت لعبة جديدة: {0}" -f $Name)
    }
    return $true
}

function Get-ActivePowerScheme{
    try{$t=(& powercfg.exe /GETACTIVESCHEME 2>&1|Out-String);if($t -match '([0-9a-fA-F-]{36})'){return $matches[1]}}catch{};return $null
}
function Test-PowerScheme([string]$Guid){try{return ((& powercfg.exe /L 2>&1|Out-String)-match [regex]::Escape($Guid))}catch{return $false}}
function Set-PowerScheme([string]$Guid){try{& powercfg.exe /S $Guid 2>&1|Out-Null;return($LASTEXITCODE-eq 0)}catch{return $false}}
function Save-Reg([string]$Path,[string]$Name){
    $key="$Path|$Name";try{$o=Get-ItemProperty -Path $Path -Name $Name -ErrorAction Stop;$script:GameModeBackup[$key]=@{Exists=$true;Value=$o.$Name}}catch{$script:GameModeBackup[$key]=@{Exists=$false;Value=$null}}
}
function Set-GameModeForSession{
    $p='HKCU:\Software\Microsoft\GameBar';Save-Reg $p 'AutoGameModeEnabled';Save-Reg $p 'AllowAutoGameMode';New-Item $p -Force|Out-Null
    New-ItemProperty $p -Name 'AutoGameModeEnabled' -PropertyType DWord -Value 1 -Force|Out-Null
    New-ItemProperty $p -Name 'AllowAutoGameMode' -PropertyType DWord -Value 1 -Force|Out-Null
}
function Restore-GameMode{
    foreach($key in @($script:GameModeBackup.Keys)){$x=$key -split '\|',2;$p=$x[0];$n=$x[1];$d=$script:GameModeBackup[$key];try{if($d.Exists){New-Item $p -Force|Out-Null;New-ItemProperty $p -Name $n -PropertyType DWord -Value ([int]$d.Value) -Force|Out-Null}else{Remove-ItemProperty $p -Name $n -Force -ErrorAction SilentlyContinue}}catch{}}
    $script:GameModeBackup=@{}
}
function Backup-AndSetPriority($Process,[System.Diagnostics.ProcessPriorityClass]$Priority){
    try{if(-not $script:PriorityBackup.ContainsKey($Process.Id)){$script:PriorityBackup[$Process.Id]=@{Priority=$Process.PriorityClass.ToString()}};if($Process.PriorityClass-ne$Priority){$Process.PriorityClass=$Priority;Write-Log("Scheduler: {0} -> {1}" -f $Process.ProcessName,$Priority)}}catch{}
}
function Restore-Priorities{
    foreach($id in @($script:PriorityBackup.Keys)){try{$p=Get-Process -Id ([int]$id) -ErrorAction Stop;$old=[System.Diagnostics.ProcessPriorityClass][Enum]::Parse([System.Diagnostics.ProcessPriorityClass],[string]$script:PriorityBackup[$id].Priority);$p.PriorityClass=$old}catch{}}
    $script:PriorityBackup=@{}
}

function Get-ForegroundProcess{
    try{$h=[D7Win32]::GetForegroundWindow();if($h-eq[IntPtr]::Zero){return $null};[uint32]$id=0;[void][D7Win32]::GetWindowThreadProcessId($h,[ref]$id);if($id-eq 0-or$id-eq$PID){return $null};return Get-Process -Id $id -ErrorAction Stop}catch{return $null}
}
function Get-ProcessPath($Process){try{return $Process.Path}catch{};try{return (Get-CimInstance Win32_Process -Filter("ProcessId="+$Process.Id)).ExecutablePath}catch{return $null}}
function Test-ExcludedProcess($Process){if(-not$Process){return $true};foreach($n in $script:ExcludedNames){if($Process.ProcessName -ieq $n){return $true}};return $false}
function Test-LikelyGame($Process){
    if(-not$Process -or(Test-ExcludedProcess $Process)){return $false};$path=Get-ProcessPath $Process;if([string]::IsNullOrWhiteSpace($path)){return $false}
    if(@($script:LearnedGames|Where-Object{$_.Path -ieq $path}).Count){return $true}
    foreach($pat in $script:GamePathPatterns){if($path -match [regex]::Escape($pat)){if($Process.ProcessName-notmatch'(?i)(launcher|crash|report|helper|updater|setup|install)'){return $true}}}
    try{if(($Process.WorkingSet64/1GB)-ge 0.75){$s=Get-Counter -Counter("\GPU Engine(pid_{0}_*engtype_3D)\Utilization Percentage" -f $Process.Id)-ErrorAction SilentlyContinue;$gpu=($s.CounterSamples|Measure-Object CookedValue -Sum).Sum;if($gpu-ge 8){return $true}}}catch{}
    return $false
}
function Get-DetectedGame{
    if($script:SessionActive){$p=Get-Process -Id $script:GamePid -ErrorAction SilentlyContinue;if($p){return $p}}
    $fg=Get-ForegroundProcess;if($fg-and(Test-LikelyGame $fg)){return $fg}
    foreach($g in @($script:LearnedGames)){try{$n=[IO.Path]::GetFileNameWithoutExtension([string]$g.Path);$p=Get-Process -Name $n -ErrorAction SilentlyContinue|Select-Object -First 1;if($p-and(Get-ProcessPath $p)-ieq[string]$g.Path){return $p}}catch{}}
    return $null
}

function Get-NvidiaTelemetry{
    $o=[ordered]@{Available=$false;Gpu=0;Temp=0;VramUsed=0;VramTotal=0;Power=0;PowerLimit=0;Clock=0}
    try{
        $line=& nvidia-smi.exe --query-gpu=utilization.gpu,temperature.gpu,memory.used,memory.total,power.draw,power.limit,clocks.current.graphics --format=csv,noheader,nounits 2>$null|Select-Object -First 1
        if($LASTEXITCODE-eq 0 -and $line){$a=$line -split ','|ForEach-Object{$_.Trim()};$o.Available=$true;$o.Gpu=[double]$a[0];$o.Temp=[double]$a[1];$o.VramUsed=[double]$a[2];$o.VramTotal=[double]$a[3];$o.Power=[double]$a[4];$o.PowerLimit=[double]$a[5];$o.Clock=[double]$a[6]}
    }catch{}
    return [pscustomobject]$o
}
function Get-GameCpuPercent($Game){
    if(-not$Game){return 0};try{$now=Get-Date;$ct=$Game.TotalProcessorTime.TotalSeconds;if($script:LastGameCpuTime-ne$null -and $script:LastGameCpuStamp){$dt=($now-$script:LastGameCpuStamp).TotalSeconds;if($dt-gt 0){$v=(($ct-$script:LastGameCpuTime)/$dt/$script:LogicalCpu)*100;$script:LastGameCpuTime=$ct;$script:LastGameCpuStamp=$now;return [math]::Max(0,[math]::Min(100,[math]::Round($v,1)))}};$script:LastGameCpuTime=$ct;$script:LastGameCpuStamp=$now}catch{};return 0
}
function Get-SystemTelemetry{
    $game=if($script:SessionActive){Get-Process -Id $script:GamePid -ErrorAction SilentlyContinue}else{$null}
    $gpu=Get-NvidiaTelemetry
    $cpu=0;try{$cpu=[double](Get-CimInstance Win32_Processor|Measure-Object LoadPercentage -Average).Average}catch{}
    $free=0;$total=0;try{$os=Get-CimInstance Win32_OperatingSystem;$free=[math]::Round(($os.FreePhysicalMemory*1KB)/1GB,2);$total=[math]::Round(($os.TotalVisibleMemorySize*1KB)/1GB,2)}catch{}
    $gc=Get-GameCpuPercent $game
    $gr=0;try{$gr=[math]::Round($game.WorkingSet64/1GB,2)}catch{}
    return [pscustomobject]@{Time=Get-Date;Cpu=$cpu;GameCpu=$gc;Gpu=$gpu.Gpu;GpuTemp=$gpu.Temp;VramUsed=$gpu.VramUsed;VramTotal=$gpu.VramTotal;GpuPower=$gpu.Power;GpuPowerLimit=$gpu.PowerLimit;GpuClock=$gpu.Clock;FreeRam=$free;TotalRam=$total;GameRam=$gr;Nvidia=$gpu.Available}
}
function Add-TelemetryHistory($T){[void]$script:SensorHistory.Add($T);while($script:SensorHistory.Count-gt 12){$script:SensorHistory.RemoveAt(0)}}
function Avg($items,[string]$p){$v=@($items|ForEach-Object{[double]($_.$p)});if(-not$v.Count){return 0};return [math]::Round(($v|Measure-Object -Average).Average,1)}
function Get-AdaptiveState{
    if(-not$script:SessionActive){return [pscustomobject]@{State='IDLE';Confidence=100;Reason='لا توجد لعبة نشطة'}}
    $h=@($script:SensorHistory);if($h.Count-lt 3){return [pscustomobject]@{State='LEARNING';Confidence=40;Reason='نجمع baseline للجلسة'}}
    $gpu=Avg $h 'Gpu';$cpu=Avg $h 'Cpu';$gc=Avg $h 'GameCpu';$temp=Avg $h 'GpuTemp';$free=Avg $h 'FreeRam';$bg=[math]::Max(0,$cpu-$gc)
    if($temp-ge 82){return [pscustomobject]@{State='THERMAL_LIMIT';Confidence=[math]::Min(99,[int](70+($temp-82)*6));Reason=("GPU حرارة مرتفعة ≈ {0}°C" -f $temp)}}
    if($free-gt 0 -and $free-lt 2.0){return [pscustomobject]@{State='MEMORY_PRESSURE';Confidence=[math]::Min(99,[int](85+(2-$free)*7));Reason=("RAM المتاحة ≈ {0}GB" -f $free)}}
    if($gpu-ge 94 -and $gc-lt 70){return [pscustomobject]@{State='GPU_BOUND';Confidence=[math]::Min(99,[int](75+($gpu-94)*4));Reason=("GPU ≈ {0}%" -f $gpu)}}
    if($gc-ge 65 -and $gpu-lt 90){return [pscustomobject]@{State='CPU_BOUND';Confidence=[math]::Min(98,[int](70+($gc-65)));Reason=("Game CPU ≈ {0}% / GPU ≈ {1}%" -f $gc,$gpu)}}
    if($bg-ge 30 -and $cpu-ge 70){return [pscustomobject]@{State='BACKGROUND_INTERFERENCE';Confidence=[math]::Min(95,[int](65+$bg/2));Reason=("حمل خلفي تقريبي ≈ {0}%" -f [math]::Round($bg,0))}}
    return [pscustomobject]@{State='BALANCED';Confidence=75;Reason='لا يوجد اختناق واضح الآن'}
}

function Get-ProfileKey{if($script:GamePath){return ([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($script:GamePath))).TrimEnd('=')}else{return $script:GameName}}
function Update-Learning($State){
    if(-not$script:SessionActive){return};$k=Get-ProfileKey;if(-not$script:Profiles.ContainsKey($k)){$script:Profiles[$k]=[pscustomobject]@{Name=$script:GameName;Path=$script:GamePath;Sessions=0;Samples=0;GPU_BOUND=0;CPU_BOUND=0;THERMAL_LIMIT=0;MEMORY_PRESSURE=0;BACKGROUND_INTERFERENCE=0;BALANCED=0;LastSeen='';LastPolicy='Observe'}}
    $p=$script:Profiles[$k];$p.Samples=[int]$p.Samples+1;if($p.PSObject.Properties.Name -contains $State){$p.$State=[int]$p.$State+1};$p.LastSeen=(Get-Date).ToString('o')
    if(((Get-Date)-$script:LastProfileSave).TotalSeconds-ge 30){Save-Profiles}
}
function Apply-BackgroundRelief([bool]$Aggressive=$false){
    foreach($n in $script:BackgroundCandidates){Get-Process -Name $n -ErrorAction SilentlyContinue|ForEach-Object{Backup-AndSetPriority $_ ([System.Diagnostics.ProcessPriorityClass]::BelowNormal)}}
    if($Aggressive){Get-Process -Name 'chrome','msedge','firefox' -ErrorAction SilentlyContinue|ForEach-Object{Backup-AndSetPriority $_ ([System.Diagnostics.ProcessPriorityClass]::BelowNormal)}}
}
function Apply-AdaptivePolicy($S){
    if(-not$script:SessionActive){return};if(((Get-Date)-$script:LastAdaptiveAction).TotalSeconds-lt 12){return}
    switch($S.State){
        'MEMORY_PRESSURE' {Apply-BackgroundRelief $true;Write-Log('Adaptive: Memory Pressure -> خفض أولوية المتصفحات/المحدثات فقط.');$script:LastAdaptiveAction=Get-Date}
        'BACKGROUND_INTERFERENCE' {Apply-BackgroundRelief $false;Write-Log('Adaptive: Background Interference -> خفض المحدثات والخدمات غير الحرجة المعروفة.');$script:LastAdaptiveAction=Get-Date}
        'THERMAL_LIMIT' {Write-Log('Adaptive: Thermal Limit -> لا كسر سرعة تلقائي. أوصي بتحسين التبريد/منحنى المراوح؛ D7 لن يفرض Power Limit بدون تحقق.');$script:LastAdaptiveAction=Get-Date}
        'GPU_BOUND' { }
        'CPU_BOUND' {Apply-BackgroundRelief $false;$script:LastAdaptiveAction=Get-Date}
    }
}

function Start-GameSession($Game){
    if($script:SessionActive-or-not$Game){return};$script:SessionActive=$true;$script:GamePid=$Game.Id;$script:GameName=$Game.ProcessName;$script:GamePath=Get-ProcessPath $Game;$script:SensorHistory.Clear();$script:LastGameCpuTime=$null;$script:LastGameCpuStamp=$null
    Write-Log("=== ADAPTIVE GAME START: {0} PID={1} ===" -f $script:GameName,$script:GamePid)
    $script:OriginalScheme=Get-ActivePowerScheme;$hp='8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c';if(Test-PowerScheme $hp){if(Set-PowerScheme $hp){Write-Log 'Power: High performance مؤقتًا.'}}
    Set-GameModeForSession;Backup-AndSetPriority $Game ([System.Diagnostics.ProcessPriorityClass]::AboveNormal)
    if($script:GamePath){try{$gp='HKCU:\Software\Microsoft\DirectX\UserGpuPreferences';New-Item $gp -Force|Out-Null;New-ItemProperty $gp -Name $script:GamePath -PropertyType String -Value 'GpuPreference=2;' -Force|Out-Null}catch{}}
    $k=Get-ProfileKey;if($script:Profiles.ContainsKey($k)){$script:Profiles[$k].Sessions=[int]$script:Profiles[$k].Sessions+1}
    if($script:StatusLabel){$script:StatusLabel.Text="Adaptive Session — $($script:GameName)";$script:StatusLabel.ForeColor=[Drawing.Color]::LawnGreen}
}
function Stop-GameSession{
    if(-not$script:SessionActive){return};Write-Log("=== GAME END: {0} ===" -f $script:GameName);Restore-Priorities;Restore-GameMode;if($script:OriginalScheme){[void](Set-PowerScheme $script:OriginalScheme)};Save-Profiles
    $script:SessionActive=$false;$script:GamePid=0;$script:GameName='';$script:GamePath='';$script:OriginalScheme=$null;$script:SensorHistory.Clear();$script:LastState='IDLE';$script:LastConfidence=0
    if($script:StatusLabel){$script:StatusLabel.Text='Adaptive Watch جاهز — افتح أي لعبة';$script:StatusLabel.ForeColor=[Drawing.Color]::DeepSkyBlue}
}

function Compare-Version([string]$A,[string]$B){try{return ([version]$A).CompareTo([version]$B)}catch{return 0}}
function Get-CurrentExePath{try{$p=[Environment]::GetCommandLineArgs()[0];if($p-and(Test-Path $p)){return (Resolve-Path $p).Path}}catch{};return $null}
function Check-OnlineUpdate([bool]$Interactive=$false){
    try{
        [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
        $m=Invoke-RestMethod -Uri ($ManifestUrl+'?t='+[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) -UseBasicParsing -TimeoutSec 10
        if(-not$m.version -or -not$m.packageUrl -or -not$m.sha256){throw 'manifest ناقص'}
        if((Compare-Version ([string]$m.version) $Version)-le 0){if($Interactive){[Windows.Forms.MessageBox]::Show("أنت على أحدث إصدار: $Version",'D7 Online Update')|Out-Null};return}
        Write-Log("Online Update: إصدار {0} متاح." -f $m.version)
        $ans=[Windows.Forms.DialogResult]::No
        if($Interactive -or $form.Visible){$ans=[Windows.Forms.MessageBox]::Show("تحديث D7 $($m.version) متاح عبر الإنترنت فقط.\n\n$($m.changelogAr)\n\nتنزيل وتثبيت الآن؟",'D7 Online Update',[Windows.Forms.MessageBoxButtons]::YesNo,[Windows.Forms.MessageBoxIcon]::Information)}
        if($ans-ne[Windows.Forms.DialogResult]::Yes){return}
        Install-OnlineUpdate $m
    }catch{Write-Log("Online Update check failed: {0}" -f $_.Exception.Message);if($Interactive){[Windows.Forms.MessageBox]::Show('تعذر فحص التحديث عبر الإنترنت الآن.','D7 Online Update')|Out-Null}}
}
function Install-OnlineUpdate($Manifest){
    $ver=[string]$Manifest.version;$dir=Join-Path $UpdateDir $ver;Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue;New-Item $dir -ItemType Directory -Force|Out-Null
    $zip=Join-Path $dir 'package.zip';Write-Log("Update: downloading $ver...");Invoke-WebRequest -Uri ([string]$Manifest.packageUrl) -OutFile $zip -UseBasicParsing -TimeoutSec 120
    $hash=(Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant();if($hash-ne([string]$Manifest.sha256).ToLowerInvariant()){Remove-Item $zip -Force -ErrorAction SilentlyContinue;throw 'SHA256 mismatch'}
    $payload=Join-Path $dir 'payload';Expand-Archive $zip $payload -Force;$newExe=Get-ChildItem $payload -Recurse -File -Filter 'D7_Gaming_Engine.exe'|Select-Object -First 1;if(-not$newExe){throw 'EXE missing in package'}
    $current=Get-CurrentExePath;if(-not$current){throw 'Current EXE path unavailable'};$helper=Join-Path $dir 'apply-update.ps1';$pidNow=$PID
    $code=@"
`$ErrorActionPreference='Stop'
try { Wait-Process -Id $pidNow -Timeout 30 -ErrorAction SilentlyContinue } catch {}
Start-Sleep -Milliseconds 700
Copy-Item -LiteralPath '$($newExe.FullName.Replace("'","''"))' -Destination '$($current.Replace("'","''"))' -Force
Start-Process -FilePath '$($current.Replace("'","''"))'
"@
    Set-Content $helper $code -Encoding UTF8;Write-Log 'Update verified. Restarting into new version.';Start-Process powershell.exe -WindowStyle Hidden -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$helper+'"'));$script:Exiting=$true;Stop-GameSession;$tray.Visible=$false;$form.Close()
}

function Scan-System{
    Write-Log '--- Deep Adaptive Scan ---'
    try{$c=Get-CimInstance Win32_Processor|Select-Object -First 1;Write-Log("CPU: {0} | {1}C/{2}T" -f $c.Name.Trim(),$c.NumberOfCores,$c.NumberOfLogicalProcessors)}catch{}
    try{$g=Get-NvidiaTelemetry;if($g.Available){Write-Log("GPU: NVIDIA | Util={0}% Temp={1}C VRAM={2}/{3}MB Clock={4}MHz Power={5}/{6}W" -f $g.Gpu,$g.Temp,$g.VramUsed,$g.VramTotal,$g.Clock,$g.Power,$g.PowerLimit)}}catch{}
    try{$t=Get-SystemTelemetry;Write-Log("RAM: {0}GB total / {1}GB free | CPU total={2}%" -f $t.TotalRam,$t.FreeRam,$t.Cpu)}catch{}
    Write-Log("Online-only updater: {0}" -f $ManifestUrl);Write-Log 'Legacy unsafe tweaks: OFF.'
}
function Learn-CurrentGame{
    Write-Log 'تعلم لعبة: D7 سيختفي 5 ثوانٍ؛ ارجع للعبة.';$form.Hide();Start-Sleep 5;$p=Get-ForegroundProcess;$form.Show();$form.Activate();if(-not$p-or(Test-ExcludedProcess $p)){Write-Log 'تعلم لعبة: لم ألتقط لعبة صالحة.';return};$path=Get-ProcessPath $p;if(Add-LearnedGame $path $p.ProcessName){[Windows.Forms.MessageBox]::Show("تم تعلم $($p.ProcessName)",'D7')|Out-Null;if(-not$script:SessionActive){Start-GameSession $p}}
}
function Enable-Startup{try{$exe=Get-CurrentExePath;if($exe){$r='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run';New-Item $r -Force|Out-Null;New-ItemProperty $r -Name 'D7GamingEngine' -PropertyType String -Value ('"{0}" --background' -f $exe) -Force|Out-Null;return $true}}catch{};return $false}
function Disable-Startup{try{Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'D7GamingEngine' -Force -ErrorAction SilentlyContinue;return $true}catch{return $false}}
function Test-StartupEnabled{try{return -not[string]::IsNullOrWhiteSpace((Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'D7GamingEngine' -ErrorAction Stop).D7GamingEngine)}catch{return $false}}

Load-State
$form=New-Object Windows.Forms.Form;$form.Text="D7 Gaming Engine v$Version — Adaptive Performance Engine";$form.Size=New-Object Drawing.Size(1120,760);$form.StartPosition='CenterScreen';$form.BackColor=[Drawing.Color]::FromArgb(10,14,18);$form.ForeColor=[Drawing.Color]::White;$form.RightToLeft='Yes';$form.RightToLeftLayout=$true;$form.Font=New-Object Drawing.Font('Segoe UI',10)
$title=New-Object Windows.Forms.Label;$title.Text='D7 ADAPTIVE PERFORMANCE ENGINE';$title.Font=New-Object Drawing.Font('Segoe UI Semibold',22,[Drawing.FontStyle]::Bold);$title.ForeColor=[Drawing.Color]::Chartreuse;$title.AutoSize=$true;$title.Location=New-Object Drawing.Point(625,20);$form.Controls.Add($title)
$status=New-Object Windows.Forms.Label;$status.Text='Adaptive Watch جاهز — افتح أي لعبة';$status.AutoSize=$true;$status.Location=New-Object Drawing.Point(700,70);$status.ForeColor=[Drawing.Color]::DeepSkyBlue;$form.Controls.Add($status);$script:StatusLabel=$status
$state=New-Object Windows.Forms.Label;$state.Text='STATE: IDLE | Confidence 100%';$state.Font=New-Object Drawing.Font('Consolas',11,[Drawing.FontStyle]::Bold);$state.AutoSize=$true;$state.Location=New-Object Drawing.Point(680,100);$state.ForeColor=[Drawing.Color]::Gold;$form.Controls.Add($state);$script:StateLabel=$state
$reason=New-Object Windows.Forms.Label;$reason.Text='المحرك يراقب CPU/GPU/RAM/VRAM/الحرارة ويعدل سياسة الجلسة عند الحاجة.';$reason.AutoSize=$false;$reason.Size=New-Object Drawing.Size(930,38);$reason.Location=New-Object Drawing.Point(105,130);$reason.ForeColor=[Drawing.Color]::Silver;$form.Controls.Add($reason);$script:ReasonLabel=$reason

$metrics=New-Object Windows.Forms.Label;$metrics.Text='CPU -- | Game -- | GPU -- | Temp -- | VRAM -- | RAM --';$metrics.Font=New-Object Drawing.Font('Consolas',10);$metrics.AutoSize=$false;$metrics.Size=New-Object Drawing.Size(930,28);$metrics.Location=New-Object Drawing.Point(105,170);$metrics.ForeColor=[Drawing.Color]::WhiteSmoke;$form.Controls.Add($metrics);$script:MetricsLabel=$metrics

$buttons=@(
@('فحص عميق',105),@('تعلم اللعبة الحالية',290),@('تحديث أونلاين',475),@('استرجاع الجلسة',660),@('فتح السجل',845)
)
foreach($b in $buttons){$x=New-Object Windows.Forms.Button;$x.Text=$b[0];$x.Size=New-Object Drawing.Size(170,42);$x.Location=New-Object Drawing.Point([int]$b[1],210);$form.Controls.Add($x);switch($b[0]){'فحص عميق'{$script:BtnScan=$x}'تعلم اللعبة الحالية'{$script:BtnLearn=$x}'تحديث أونلاين'{$script:BtnUpdate=$x}'استرجاع الجلسة'{$script:BtnRestore=$x}'فتح السجل'{$script:BtnLog=$x}}}
$chk=New-Object Windows.Forms.CheckBox;$chk.Text='تشغيل D7 تلقائيًا مع Windows';$chk.AutoSize=$true;$chk.Location=New-Object Drawing.Point(760,268);$chk.Checked=Test-StartupEnabled;$form.Controls.Add($chk)
$policy=New-Object Windows.Forms.Label;$policy.Text='Adaptive policy: Observe → Diagnose → Adjust → Measure → Learn → Restore. لا إغلاق برامج بالقوة، ولا تعديلات BCD/HPET/Timer/RAM purge.';$policy.AutoSize=$false;$policy.Size=New-Object Drawing.Size(930,40);$policy.Location=New-Object Drawing.Point(105,300);$policy.ForeColor=[Drawing.Color]::DarkGray;$form.Controls.Add($policy)
$log=New-Object Windows.Forms.TextBox;$log.Multiline=$true;$log.ReadOnly=$true;$log.ScrollBars='Vertical';$log.BackColor=[Drawing.Color]::FromArgb(5,9,13);$log.ForeColor=[Drawing.Color]::Gainsboro;$log.Size=New-Object Drawing.Size(930,330);$log.Location=New-Object Drawing.Point(105,350);$log.RightToLeft='No';$form.Controls.Add($log);$script:LogBox=$log

$script:BtnScan.Add_Click({Scan-System});$script:BtnLearn.Add_Click({Learn-CurrentGame});$script:BtnUpdate.Add_Click({Check-OnlineUpdate $true});$script:BtnRestore.Add_Click({Stop-GameSession});$script:BtnLog.Add_Click({try{Start-Process explorer.exe -ArgumentList ('/select,"'+$LogPath+'"')}catch{}});$chk.Add_CheckedChanged({if($chk.Checked){[void](Enable-Startup)}else{[void](Disable-Startup)}})

$timer=New-Object Windows.Forms.Timer;$timer.Interval=2500;$timer.Add_Tick({
    if($script:SessionActive){$g=Get-Process -Id $script:GamePid -ErrorAction SilentlyContinue;if(-not$g){Stop-GameSession;return}}
    else{$g=Get-DetectedGame;if($g){Start-GameSession $g}}
    $t=Get-SystemTelemetry;$script:CurrentTelemetry=$t;Add-TelemetryHistory $t;$s=Get-AdaptiveState;$script:LastState=$s.State;$script:LastConfidence=$s.Confidence;Update-Learning $s.State;Apply-AdaptivePolicy $s
    if($script:StateLabel){$script:StateLabel.Text=("STATE: {0} | Confidence {1}%" -f $s.State,$s.Confidence);$script:ReasonLabel.Text=$s.Reason;$script:MetricsLabel.Text=("CPU {0,5}% | Game {1,5}% | GPU {2,5}% | Temp {3,4}C | VRAM {4,5}/{5,-5} MB | RAM free {6} GB" -f $t.Cpu,$t.GameCpu,$t.Gpu,$t.GpuTemp,$t.VramUsed,$t.VramTotal,$t.FreeRam)}
});$timer.Start()

$tray=New-Object Windows.Forms.NotifyIcon;$tray.Text="D7 Gaming Engine v$Version";$tray.Icon=[Drawing.SystemIcons]::Application;$tray.Visible=$true;$menu=New-Object Windows.Forms.ContextMenuStrip;$show=$menu.Items.Add('فتح D7');$update=$menu.Items.Add('فحص تحديث أونلاين');$exit=$menu.Items.Add('خروج واسترجاع الإعدادات');$tray.ContextMenuStrip=$menu;$show.Add_Click({$form.Show();$form.WindowState='Normal';$form.Activate()});$update.Add_Click({Check-OnlineUpdate $true});$exit.Add_Click({$script:Exiting=$true;Stop-GameSession;$timer.Stop();$tray.Visible=$false;$form.Close()});$tray.Add_DoubleClick({$form.Show();$form.WindowState='Normal';$form.Activate()})
$form.Add_FormClosing({param($sender,$e);if(-not$script:Exiting){$e.Cancel=$true;$form.Hide();$tray.ShowBalloonTip(1300,'D7 Gaming Engine','Adaptive Engine مستمر بالخلفية.','Info')}})
$form.Add_Shown({Write-Log("D7 Gaming Engine v$Version started.");Write-Log 'Adaptive Engine: telemetry + confidence states + reversible scheduler.';Write-Log 'Updates: ONLINE ONLY via signed-by-SHA256 stable manifest.';if(-not(Test-StartupEnabled)){if(Enable-Startup){$chk.Checked=$true}};Scan-System;$g=Get-DetectedGame;if($g){Start-GameSession $g};if([Environment]::GetCommandLineArgs()-contains'--background'){$form.Hide()};$updateTimer=New-Object Windows.Forms.Timer;$updateTimer.Interval=3500;$updateTimer.Add_Tick({$updateTimer.Stop();Check-OnlineUpdate $false});$updateTimer.Start();$script:StartupUpdateTimer=$updateTimer})
[void]$form.ShowDialog();if($script:SessionActive){Stop-GameSession};$tray.Visible=$false
