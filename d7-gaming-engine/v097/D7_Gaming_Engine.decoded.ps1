# D7 Gaming Engine v0.9.7 - Universal Gaming Mode
# Target: Ryzen 5 3600 / RTX 2060 SUPER / 16 GB / Windows 10 19045 / 1080p 165 Hz
# Universal game detection + reversible gaming session + NVIDIA bridge.
# Safety: no CPU Sets/affinity, EcoQoS, timer forcing, BCD/HPET, RAM purge, custom D7 plans, Anti-Cheat/game-memory access.

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

$Version = '0.9.7'
$AppRoot = Join-Path $env:ProgramData 'D7 Gaming Engine'
$LogDir = Join-Path $AppRoot 'GamingLogs'
$GamesFile = Join-Path $AppRoot 'learned-games.json'
New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
$LogPath = Join-Path $LogDir ("D7_Universal_{0}.log" -f (Get-Date -Format 'yyyyMMdd_HHmmss'))

$script:SessionActive = $false
$script:GamePid = 0
$script:GameName = ''
$script:GamePath = ''
$script:OriginalScheme = $null
$script:PriorityBackup = @{}
$script:GameModeBackup = @{}
$script:LowRamWarned = $false
$script:Exiting = $false
$script:LearnedGames = @()
$script:LastCandidate = ''
$script:NvidiaAppPath = $null

$script:ExcludedNames = @(
    'explorer','dwm','ShellExperienceHost','StartMenuExperienceHost','SearchApp','SearchHost',
    'ApplicationFrameHost','SystemSettings','Taskmgr','powershell','pwsh','cmd','conhost',
    'chrome','msedge','firefox','Discord','obs64','steam','steamwebhelper','EpicGamesLauncher',
    'Battle.net','Agent','EADesktop','EALauncher','UbisoftConnect','upc','GalaxyClient',
    'NVIDIA App','NVIDIA Share','NVIDIA Overlay','nvcontainer','ChatGPT','devenv','Code'
)

$script:GamePathPatterns = @(
    '\\steamapps\\common\\',
    '\\SteamLibrary\\',
    '\\XboxGames\\',
    '\\Epic Games\\',
    '\\EA Games\\',
    '\\Origin Games\\',
    '\\Ubisoft\\',
    '\\GOG Galaxy\\Games\\',
    '\\Battle.net\\',
    '\\Call of Duty\\',
    '\\Games\\'
)

function Write-Log([string]$Text) {
    $line = "[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $Text
    Add-Content -Path $LogPath -Value $line -Encoding UTF8
    if ($script:LogBox -and -not $script:LogBox.IsDisposed) {
        $script:LogBox.AppendText($line + [Environment]::NewLine)
        $script:LogBox.SelectionStart = $script:LogBox.TextLength
        $script:LogBox.ScrollToCaret()
    }
}

function Load-LearnedGames {
    try {
        if (Test-Path $GamesFile) {
            $x = Get-Content $GamesFile -Raw -Encoding UTF8 | ConvertFrom-Json
            $script:LearnedGames = @($x)
        }
    } catch { $script:LearnedGames = @() }
}

function Save-LearnedGames {
    try {
        $script:LearnedGames | ConvertTo-Json -Depth 4 | Set-Content $GamesFile -Encoding UTF8
    } catch {}
}

function Add-LearnedGame([string]$Path,[string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path $Path)) { return $false }
    $exists = @($script:LearnedGames | Where-Object { $_.Path -ieq $Path })
    if (-not $exists) {
        $script:LearnedGames += [pscustomobject]@{ Name=$Name; Path=$Path; Added=(Get-Date).ToString('o') }
        Save-LearnedGames
        Write-Log ("تعلمت لعبة جديدة: {0} -> {1}" -f $Name,$Path)
    } else {
        Write-Log ("اللعبة موجودة مسبقًا في مكتبة D7: {0}" -f $Name)
    }
    return $true
}

function Get-FreeRamGB {
    try {
        $os = Get-CimInstance Win32_OperatingSystem
        return [math]::Round(($os.FreePhysicalMemory * 1KB) / 1GB, 2)
    } catch { return 0 }
}

function Get-ActivePowerScheme {
    try {
        $txt = (& powercfg.exe /GETACTIVESCHEME 2>&1 | Out-String)
        if ($txt -match '([0-9a-fA-F-]{36})') { return $matches[1] }
    } catch {}
    return $null
}

function Test-PowerScheme([string]$Guid) {
    try {
        $txt = (& powercfg.exe /L 2>&1 | Out-String)
        return ($txt -match [regex]::Escape($Guid))
    } catch { return $false }
}

function Set-PowerScheme([string]$Guid) {
    if ([string]::IsNullOrWhiteSpace($Guid)) { return $false }
    try {
        & powercfg.exe /S $Guid 2>&1 | Out-Null
        return ($LASTEXITCODE -eq 0)
    } catch { return $false }
}

function Save-RegistryValue([string]$Path,[string]$Name) {
    $key = "$Path|$Name"
    try {
        $obj = Get-ItemProperty -Path $Path -Name $Name -ErrorAction Stop
        $script:GameModeBackup[$key] = @{ Exists=$true; Value=$obj.$Name }
    } catch {
        $script:GameModeBackup[$key] = @{ Exists=$false; Value=$null }
    }
}

function Set-GameModeForSession {
    $path = 'HKCU:\Software\Microsoft\GameBar'
    Save-RegistryValue $path 'AutoGameModeEnabled'
    Save-RegistryValue $path 'AllowAutoGameMode'
    New-Item -Path $path -Force | Out-Null
    New-ItemProperty -Path $path -Name 'AutoGameModeEnabled' -PropertyType DWord -Value 1 -Force | Out-Null
    New-ItemProperty -Path $path -Name 'AllowAutoGameMode' -PropertyType DWord -Value 1 -Force | Out-Null
    Write-Log 'Windows Game Mode: مفعّل للجلسة الحالية.'
}

function Restore-GameMode {
    foreach ($key in @($script:GameModeBackup.Keys)) {
        $parts = $key -split '\|',2
        $path=$parts[0]; $name=$parts[1]; $data=$script:GameModeBackup[$key]
        try {
            if ($data.Exists) {
                New-Item -Path $path -Force | Out-Null
                New-ItemProperty -Path $path -Name $name -PropertyType DWord -Value ([int]$data.Value) -Force | Out-Null
            } else {
                Remove-ItemProperty -Path $path -Name $name -Force -ErrorAction SilentlyContinue
            }
        } catch {}
    }
    $script:GameModeBackup=@{}
}

function Backup-AndSetPriority($Process,[System.Diagnostics.ProcessPriorityClass]$Priority) {
    try {
        if (-not $script:PriorityBackup.ContainsKey($Process.Id)) {
            $script:PriorityBackup[$Process.Id] = @{ Priority=$Process.PriorityClass.ToString() }
        }
        if ($Process.PriorityClass -ne $Priority) {
            $Process.PriorityClass=$Priority
            Write-Log ("Priority: {0} PID={1} -> {2}" -f $Process.ProcessName,$Process.Id,$Priority)
        }
    } catch {}
}

function Restore-Priorities {
    foreach ($pidKey in @($script:PriorityBackup.Keys)) {
        try {
            $p=Get-Process -Id ([int]$pidKey) -ErrorAction Stop
            $old=[System.Diagnostics.ProcessPriorityClass][Enum]::Parse([System.Diagnostics.ProcessPriorityClass],[string]$script:PriorityBackup[$pidKey].Priority)
            $p.PriorityClass=$old
        } catch {}
    }
    $script:PriorityBackup=@{}
}

function Get-ForegroundProcess {
    try {
        $hwnd=[D7Win32]::GetForegroundWindow()
        if ($hwnd -eq [IntPtr]::Zero) { return $null }
        [uint32]$pid=0
        [void][D7Win32]::GetWindowThreadProcessId($hwnd,[ref]$pid)
        if ($pid -eq 0 -or $pid -eq $PID) { return $null }
        return Get-Process -Id $pid -ErrorAction Stop
    } catch { return $null }
}

function Get-ProcessPath($Process) {
    try { return $Process.Path } catch {}
    try {
        $c=Get-CimInstance Win32_Process -Filter ("ProcessId="+$Process.Id)
        return $c.ExecutablePath
    } catch { return $null }
}

function Test-ExcludedProcess($Process) {
    if (-not $Process) { return $true }
    foreach ($n in $script:ExcludedNames) {
        if ($Process.ProcessName -ieq $n) { return $true }
    }
    return $false
}

function Test-LearnedPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    return [bool](@($script:LearnedGames | Where-Object { $_.Path -ieq $Path }).Count)
}

function Test-LikelyGame($Process) {
    if (-not $Process -or (Test-ExcludedProcess $Process)) { return $false }
    $path=Get-ProcessPath $Process
    if ([string]::IsNullOrWhiteSpace($path)) { return $false }
    if (Test-LearnedPath $path) { return $true }

    foreach ($pat in $script:GamePathPatterns) {
        if ($path -match [regex]::Escape($pat)) {
            if ($Process.ProcessName -notmatch '(?i)(launcher|crash|report|helper|updater|setup|install)') { return $true }
        }
    }

    try {
        $mem=[double]$Process.WorkingSet64/1GB
        if ($mem -ge 0.75) {
            $samples = Get-Counter -Counter ("\GPU Engine(pid_{0}_*engtype_3D)\Utilization Percentage" -f $Process.Id) -ErrorAction SilentlyContinue
            $gpu=($samples.CounterSamples | Measure-Object CookedValue -Sum).Sum
            if ($gpu -ge 8) { return $true }
        }
    } catch {}
    return $false
}

function Get-DetectedGame {
    if ($script:SessionActive) {
        $p=Get-Process -Id $script:GamePid -ErrorAction SilentlyContinue
        if ($p) { return $p }
    }

    # First choice: foreground process. This detects almost every story/Steam/Epic/Xbox game.
    $fg=Get-ForegroundProcess
    if ($fg -and (Test-LikelyGame $fg)) { return $fg }

    # Second choice: learned games that are currently running.
    foreach ($g in @($script:LearnedGames)) {
        try {
            $name=[IO.Path]::GetFileNameWithoutExtension([string]$g.Path)
            $p=Get-Process -Name $name -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($p -and (Get-ProcessPath $p) -ieq [string]$g.Path) { return $p }
        } catch {}
    }

    # Third choice: common game library paths among heavy visible-ish processes.
    try {
        $cims=Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.WorkingSetSize -gt 700MB }
        foreach ($c in $cims) {
            $path=[string]$c.ExecutablePath
            $match=$false
            foreach ($pat in $script:GamePathPatterns) { if ($path -match [regex]::Escape($pat)) { $match=$true; break } }
            if ($match -and $c.Name -notmatch '(?i)(launcher|crash|report|helper|updater)') {
                try {
                    $p=Get-Process -Id $c.ProcessId -ErrorAction Stop
                    if (-not (Test-ExcludedProcess $p)) { return $p }
                } catch {}
            }
        }
    } catch {}
    return $null
}

function Tune-BackgroundForGaming {
    $free=Get-FreeRamGB
    $alwaysLow=@('fdm','Urban Vpn Updater','OneDriveStandaloneUpdater','AdobeARM','CCXProcess')
    foreach ($name in $alwaysLow) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
            Backup-AndSetPriority $_ ([System.Diagnostics.ProcessPriorityClass]::BelowNormal)
        }
    }
    if ($free -gt 0 -and $free -lt 4.0) {
        Get-Process -Name 'chrome','msedge' -ErrorAction SilentlyContinue | ForEach-Object {
            Backup-AndSetPriority $_ ([System.Diagnostics.ProcessPriorityClass]::BelowNormal)
        }
        Write-Log ("Smart RAM Guard: Free RAM={0}GB؛ خفضنا أولوية المتصفح فقط بدون إغلاق أو RAM purge." -f $free)
    }
}

function Start-GameSession($Game) {
    if ($script:SessionActive -or -not $Game) { return }
    $script:SessionActive=$true
    $script:GamePid=$Game.Id
    $script:GameName=$Game.ProcessName
    $script:GamePath=Get-ProcessPath $Game
    $script:LowRamWarned=$false

    Write-Log ("=== GAME START: {0} PID={1} ===" -f $script:GameName,$script:GamePid)
    Write-Log ("Game path: {0}" -f $script:GamePath)

    $script:OriginalScheme=Get-ActivePowerScheme
    $highPerf='8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'
    if (Test-PowerScheme $highPerf) {
        if (Set-PowerScheme $highPerf) { Write-Log 'Power: Windows High performance مؤقتًا.' }
    }

    Set-GameModeForSession
    Backup-AndSetPriority $Game ([System.Diagnostics.ProcessPriorityClass]::AboveNormal)
    Tune-BackgroundForGaming

    # Windows graphics preference only. On a single RTX GPU this is harmless/redundant,
    # but it helps systems with multiple adapters. It does not edit NVIDIA profile DB.
    if ($script:GamePath) {
        try {
            $gp='HKCU:\Software\Microsoft\DirectX\UserGpuPreferences'
            New-Item -Path $gp -Force | Out-Null
            New-ItemProperty -Path $gp -Name $script:GamePath -PropertyType String -Value 'GpuPreference=2;' -Force | Out-Null
            Write-Log 'Windows GPU preference: High performance GPU لهذا الملف.'
        } catch {}
    }

    if ($script:StatusLabel) {
        $script:StatusLabel.Text="وضع اللعب نشط — $($script:GameName)"
        $script:StatusLabel.ForeColor=[System.Drawing.Color]::LawnGreen
    }
}

function Maintain-GameSession {
    if (-not $script:SessionActive) { return }
    $game=Get-Process -Id $script:GamePid -ErrorAction SilentlyContinue
    if (-not $game) { Stop-GameSession; return }
    try {
        if ($game.PriorityClass -ne [System.Diagnostics.ProcessPriorityClass]::AboveNormal) {
            $game.PriorityClass=[System.Diagnostics.ProcessPriorityClass]::AboveNormal
        }
    } catch {}
    $free=Get-FreeRamGB
    if ($free -gt 0 -and $free -lt 2.0 -and -not $script:LowRamWarned) {
        $script:LowRamWarned=$true
        Write-Log ("RAM warning: Free={0}GB. لا Standby purge؛ فقط خفض برامج الخلفية." -f $free)
        Tune-BackgroundForGaming
    }
}

function Stop-GameSession {
    if (-not $script:SessionActive) { return }
    Write-Log ("=== GAME END: {0} ===" -f $script:GameName)
    Restore-Priorities
    Restore-GameMode
    if ($script:OriginalScheme) {
        if (Set-PowerScheme $script:OriginalScheme) { Write-Log 'Power: استعدنا خطة الطاقة السابقة.' }
    }
    $script:SessionActive=$false
    $script:GamePid=0
    $script:GameName=''
    $script:GamePath=''
    $script:OriginalScheme=$null
    if ($script:StatusLabel) {
        $script:StatusLabel.Text='المراقبة العامة جاهزة — افتح أي لعبة'
        $script:StatusLabel.ForeColor=[System.Drawing.Color]::DeepSkyBlue
    }
}

function Find-NvidiaApp {
    $candidates=@(
        "$env:ProgramFiles\NVIDIA Corporation\NVIDIA app\CEF\NVIDIA App.exe",
        "$env:ProgramFiles\NVIDIA Corporation\NVIDIA app\NVIDIA App.exe",
        "$env:ProgramFiles\NVIDIA Corporation\NVIDIA App\CEF\NVIDIA App.exe"
    )
    foreach ($p in $candidates) { if (Test-Path $p) { return $p } }
    try {
        $roots=@(
            'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
            'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
        )
        foreach ($r in $roots) {
            foreach ($x in Get-ItemProperty $r -ErrorAction SilentlyContinue) {
                if ([string]$x.DisplayName -match '(?i)^NVIDIA App') {
                    $loc=[string]$x.InstallLocation
                    if ($loc -and (Test-Path $loc)) {
                        $exe=Get-ChildItem $loc -Filter '*.exe' -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '(?i)NVIDIA.*App' } | Select-Object -First 1
                        if ($exe) { return $exe.FullName }
                    }
                }
            }
        }
    } catch {}
    return $null
}

function Show-NvidiaCenter {
    $driver=''
    try {
        $smi=& nvidia-smi.exe --query-gpu=name,driver_version,memory.total,utilization.gpu,temperature.gpu --format=csv,noheader,nounits 2>$null
        if ($LASTEXITCODE -eq 0) { $driver=$smi | Select-Object -First 1 }
    } catch {}
    $app=Find-NvidiaApp
    $appState=if($app){"موجود: $app"}else{'لم أجد NVIDIA App تلقائيًا.'}
    $msg=@"
NVIDIA CENTER — جهازك RTX 2060 SUPER

GPU / Driver:
$driver

NVIDIA App:
$appState

D7 يقدر الآن:
• يكتشف أي لعبة ويطبق Windows Gaming Session تلقائيًا.
• يحدد High-performance GPU للعبة من Windows.
• يفتح NVIDIA App مباشرة.
• يعرض إعدادات Driver آمنة مقترحة.

المهم:
NVIDIA App لا توفر واجهة Automation عامة موثقة لكل Program Settings لطرف ثالث.
لذلك D7 0.9.7 لا يغيّر قاعدة NVIDIA Driver Profiles سرًا أو بإعدادات مجهولة.

إعدادات NVIDIA العامة المقترحة:
• Power Management: Normal عالميًا. استخدم Prefer maximum performance للعبة فقط إذا لاحظت هبوط clocks/stutter.
• Low Latency Mode: اتركه Off إذا اللعبة فيها NVIDIA Reflex واستخدم Reflex داخل اللعبة.
• Shader Cache Size: Driver Default أو 10GB/Unlimited إذا عندك مساحة وتواجه shader stutter.
• Texture Filtering Quality: Quality.
• Threaded Optimization: Auto.
• V-Sync: حسب VRR/G-SYNC؛ لا نفرضه عالميًا.
• Max Frame Rate: الأفضل من داخل اللعبة؛ مع VRR على 165Hz ابدأ 160 FPS، والتختيم الثقيل استهدف cap ثابت 90/120 إذا هذا أكثر سلاسة.

أقدر في إصدار لاحق إضافة NVAPI helper مخصص لتعديل Program Settings مباشرة مع Backup/Restore، بدل التحكم الهش بواجهة NVIDIA App.
"@
    [System.Windows.Forms.MessageBox]::Show($msg,'D7 — NVIDIA Center',[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
    if ($app) {
        $ans=[System.Windows.Forms.MessageBox]::Show('فتح NVIDIA App الآن؟','D7 — NVIDIA Center',[System.Windows.Forms.MessageBoxButtons]::YesNo,[System.Windows.Forms.MessageBoxIcon]::Question)
        if ($ans -eq [System.Windows.Forms.DialogResult]::Yes) {
            try { Start-Process $app } catch {}
        }
    }
}

function Show-UniversalSettings {
    $name=if($script:SessionActive){$script:GameName}else{'اللعبة الحالية'}
    $msg=@"
إعدادات D7 العامة لـ $name — 1080p / 165Hz / RTX 2060 SUPER

Preset 1 — تختيم / جودة:
• 1080p Native أو DLSS Quality إذا متوفر.
• Textures: High غالبًا مناسبة لـ8GB VRAM، وانزل Normal إذا اللعبة تتجاوز VRAM.
• Shadows: Medium.
• Volumetrics: Medium أو Low في الألعاب الثقيلة.
• Ray Tracing: Off غالبًا على RTX 2060 SUPER إذا هدفك frame-time ثابت.
• Motion Blur / Film Grain / Chromatic Aberration: Off حسب ذوقك.
• Cap: 90 أو 120 إذا اللعبة لا تثبت قرب 165. الثبات أفضل من FPS متذبذب.

Preset 2 — Balanced:
• DLSS Quality/Balanced حسب اللعبة.
• Textures High، Shadows Low/Medium، Volumetrics Low.
• Cap على رقم تقدر اللعبة تثبته أغلب الوقت.

Preset 3 — تنافسي:
• أقل Shadows/Volumetrics/Reflections.
• Reflex On أو On+Boost إذا موجود.
• إذا VRR/G-SYNC شغال: ابدأ Cap 160 على شاشة 165Hz.

قاعدة D7:
لا نرفع كل شيء Low بشكل أعمى. نحافظ على Textures/وضوح الصورة، ونخفض الإعدادات الثقيلة على GPU/CPU التي تسبب frame-time spikes.
"@
    [System.Windows.Forms.MessageBox]::Show($msg,'D7 — إعدادات اللعبة الحالية',[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
}

function Learn-CurrentGame {
    Write-Log 'تعلم لعبة: سيتم إخفاء D7 لمدة 5 ثوانٍ. افتح/ارجع للعبة الآن.'
    $form.Hide()
    Start-Sleep -Seconds 5
    $p=Get-ForegroundProcess
    $form.Show(); $form.Activate()
    if (-not $p) { Write-Log 'تعلم لعبة: لم ألتقط برنامجًا.'; return }
    if (Test-ExcludedProcess $p) { Write-Log ("تعلم لعبة: التقطت برنامجًا مستبعدًا: {0}" -f $p.ProcessName); return }
    $path=Get-ProcessPath $p
    if (Add-LearnedGame $path $p.ProcessName) {
        [System.Windows.Forms.MessageBox]::Show("تم حفظ $($p.ProcessName) كلعبة. D7 سيتعرف عليها مستقبلًا.",'D7',[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
        if (-not $script:SessionActive) { Start-GameSession $p }
    } else {
        Write-Log 'تعلم لعبة: تعذر حفظ الملف.'
    }
}

function Scan-System {
    Write-Log '--- فحص الجهاز ---'
    try {
        $cpu=Get-CimInstance Win32_Processor | Select-Object -First 1
        Write-Log ("CPU: {0} | Cores={1} Threads={2}" -f $cpu.Name.Trim(),$cpu.NumberOfCores,$cpu.NumberOfLogicalProcessors)
    } catch {}
    try {
        $gpu=Get-CimInstance Win32_VideoController | Where-Object { $_.Name -match 'NVIDIA|AMD|Intel' } | Select-Object -First 1
        Write-Log ("GPU: {0} | WMI VRAM≈{1:N1}GB" -f $gpu.Name,($gpu.AdapterRAM/1GB))
    } catch {}
    try {
        $cs=Get-CimInstance Win32_ComputerSystem
        Write-Log ("RAM: {0:N1}GB | Free={1}GB" -f ($cs.TotalPhysicalMemory/1GB),(Get-FreeRamGB))
    } catch {}
    try {
        $smi=& nvidia-smi.exe --query-gpu=driver_version,memory.total,memory.used,utilization.gpu,temperature.gpu --format=csv,noheader,nounits 2>$null
        if ($LASTEXITCODE -eq 0) { Write-Log ("NVIDIA: {0}" -f ($smi|Select-Object -First 1)) }
    } catch {}
    Write-Log ("Power GUID: {0}" -f (Get-ActivePowerScheme))
    Write-Log ("Learned games: {0}" -f @($script:LearnedGames).Count)
    $g=Get-DetectedGame
    if ($g) { Write-Log ("Game detected: {0} PID={1}" -f $g.ProcessName,$g.Id) } else { Write-Log 'Game: لا توجد لعبة مكتشفة الآن.' }
    Write-Log 'Unsafe legacy tweaks = OFF.'
}

function Enable-Startup {
    try {
        $exe=[Environment]::GetCommandLineArgs()[0]
        if ($exe -and (Test-Path $exe)) {
            $run='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
            New-Item -Path $run -Force | Out-Null
            New-ItemProperty -Path $run -Name 'D7GamingEngine' -PropertyType String -Value ('"{0}" --background' -f $exe) -Force | Out-Null
            Write-Log 'Startup: D7 EXE مباشرة بدون PowerShell.'
            return $true
        }
    } catch {}
    return $false
}

function Disable-Startup {
    try {
        Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'D7GamingEngine' -Force -ErrorAction SilentlyContinue
        Write-Log 'Startup: تم التعطيل.'
        return $true
    } catch { return $false }
}

function Test-StartupEnabled {
    try {
        $v=(Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'D7GamingEngine' -ErrorAction Stop).D7GamingEngine
        return -not [string]::IsNullOrWhiteSpace($v)
    } catch { return $false }
}

Load-LearnedGames
$script:NvidiaAppPath=Find-NvidiaApp

$form=New-Object System.Windows.Forms.Form
$form.Text="D7 Gaming Engine v$Version — Universal Gaming Mode"
$form.Size=New-Object System.Drawing.Size(1040,720)
$form.StartPosition='CenterScreen'
$form.BackColor=[System.Drawing.Color]::FromArgb(12,16,20)
$form.ForeColor=[System.Drawing.Color]::White
$form.RightToLeft='Yes'
$form.RightToLeftLayout=$true
$form.Font=New-Object System.Drawing.Font('Segoe UI',10)

$title=New-Object System.Windows.Forms.Label
$title.Text='D7 UNIVERSAL GAMING MODE'
$title.Font=New-Object System.Drawing.Font('Segoe UI Semibold',22,[System.Drawing.FontStyle]::Bold)
$title.ForeColor=[System.Drawing.Color]::Chartreuse
$title.AutoSize=$true
$title.Location=New-Object System.Drawing.Point(610,25)
$form.Controls.Add($title)

$sub=New-Object System.Windows.Forms.Label
$sub.Text='أي لعبة — Steam / Xbox / Epic / Battle.net / تختيم / تنافسي — جلسة تلقائية قابلة للاسترجاع'
$sub.AutoSize=$true
$sub.Location=New-Object System.Drawing.Point(350,72)
$sub.ForeColor=[System.Drawing.Color]::Silver
$form.Controls.Add($sub)

$status=New-Object System.Windows.Forms.Label
$status.Text='المراقبة العامة جاهزة — افتح أي لعبة'
$status.AutoSize=$true
$status.Location=New-Object System.Drawing.Point(650,105)
$status.ForeColor=[System.Drawing.Color]::DeepSkyBlue
$form.Controls.Add($status)
$script:StatusLabel=$status

$btnScan=New-Object System.Windows.Forms.Button
$btnScan.Text='فحص الجهاز'
$btnScan.Size=New-Object System.Drawing.Size(170,45)
$btnScan.Location=New-Object System.Drawing.Point(835,145)
$form.Controls.Add($btnScan)

$btnLearn=New-Object System.Windows.Forms.Button
$btnLearn.Text='تعلم اللعبة الحالية'
$btnLearn.Size=New-Object System.Drawing.Size(170,45)
$btnLearn.Location=New-Object System.Drawing.Point(650,145)
$form.Controls.Add($btnLearn)

$btnSettings=New-Object System.Windows.Forms.Button
$btnSettings.Text='إعدادات اللعبة الحالية'
$btnSettings.Size=New-Object System.Drawing.Size(170,45)
$btnSettings.Location=New-Object System.Drawing.Point(465,145)
$form.Controls.Add($btnSettings)

$btnNv=New-Object System.Windows.Forms.Button
$btnNv.Text='NVIDIA Center'
$btnNv.Size=New-Object System.Drawing.Size(170,45)
$btnNv.Location=New-Object System.Drawing.Point(280,145)
$form.Controls.Add($btnNv)

$btnRestore=New-Object System.Windows.Forms.Button
$btnRestore.Text='استرجاع الجلسة'
$btnRestore.Size=New-Object System.Drawing.Size(170,45)
$btnRestore.Location=New-Object System.Drawing.Point(95,145)
$form.Controls.Add($btnRestore)

$chkStartup=New-Object System.Windows.Forms.CheckBox
$chkStartup.Text='تشغيل D7 تلقائيًا مع Windows — EXE مباشر بدون PowerShell'
$chkStartup.AutoSize=$true
$chkStartup.Location=New-Object System.Drawing.Point(590,210)
$chkStartup.Checked=Test-StartupEnabled
$form.Controls.Add($chkStartup)

$hint=New-Object System.Windows.Forms.Label
$hint.Text='D7 لا يفترض COD. يكتشف اللعبة الأمامية/مكتبة الألعاب، أو علّمه أي EXE مرة واحدة. لا CPU Sets/Timer/RAM purge.'
$hint.AutoSize=$false
$hint.Size=New-Object System.Drawing.Size(910,40)
$hint.Location=New-Object System.Drawing.Point(95,245)
$hint.ForeColor=[System.Drawing.Color]::DarkGray
$form.Controls.Add($hint)

$log=New-Object System.Windows.Forms.TextBox
$log.Multiline=$true
$log.ReadOnly=$true
$log.ScrollBars='Vertical'
$log.BackColor=[System.Drawing.Color]::FromArgb(8,12,16)
$log.ForeColor=[System.Drawing.Color]::Gainsboro
$log.Size=New-Object System.Drawing.Size(910,335)
$log.Location=New-Object System.Drawing.Point(95,295)
$log.RightToLeft='No'
$form.Controls.Add($log)
$script:LogBox=$log

$btnScan.Add_Click({ Scan-System })
$btnLearn.Add_Click({ Learn-CurrentGame })
$btnSettings.Add_Click({ Show-UniversalSettings })
$btnNv.Add_Click({ Show-NvidiaCenter })
$btnRestore.Add_Click({ Stop-GameSession })
$chkStartup.Add_CheckedChanged({ if($chkStartup.Checked){[void](Enable-Startup)}else{[void](Disable-Startup)} })

$timer=New-Object System.Windows.Forms.Timer
$timer.Interval=2500
$timer.Add_Tick({
    if ($script:SessionActive) { Maintain-GameSession }
    else {
        $g=Get-DetectedGame
        if ($g) { Start-GameSession $g }
    }
})
$timer.Start()

$tray=New-Object System.Windows.Forms.NotifyIcon
$tray.Text="D7 Gaming Engine v$Version"
$tray.Icon=[System.Drawing.SystemIcons]::Application
$tray.Visible=$true
$menu=New-Object System.Windows.Forms.ContextMenuStrip
$itemShow=$menu.Items.Add('فتح D7')
$itemExit=$menu.Items.Add('خروج واسترجاع الإعدادات')
$tray.ContextMenuStrip=$menu
$itemShow.Add_Click({$form.Show();$form.WindowState='Normal';$form.Activate()})
$itemExit.Add_Click({
    $script:Exiting=$true
    Stop-GameSession
    $timer.Stop()
    $tray.Visible=$false
    $form.Close()
})
$tray.Add_DoubleClick({$form.Show();$form.WindowState='Normal';$form.Activate()})

$form.Add_FormClosing({
    param($sender,$e)
    if(-not $script:Exiting){
        $e.Cancel=$true
        $form.Hide()
        $tray.ShowBalloonTip(1300,'D7 Gaming Engine','D7 مستمر بالخلفية لمراقبة أي لعبة.','Info')
    }
})

$form.Add_Shown({
    Write-Log "D7 Gaming Engine v$Version started."
    Write-Log 'Universal Watch: foreground + learned games + common game libraries.'
    Write-Log ("NVIDIA App: {0}" -f $(if($script:NvidiaAppPath){'Detected'}else{'Not detected'}))
    Write-Log 'Safe reversible policy: no aggressive legacy tweaks.'
    if(-not(Test-StartupEnabled)){
        if(Enable-Startup){$chkStartup.Checked=$true}
    }
    Scan-System
    $g=Get-DetectedGame
    if($g){Start-GameSession $g}
    if([Environment]::GetCommandLineArgs() -contains '--background'){$form.Hide()}
})

[void]$form.ShowDialog()
if($script:SessionActive){Stop-GameSession}
$tray.Visible=$false
