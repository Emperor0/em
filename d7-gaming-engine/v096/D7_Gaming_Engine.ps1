# D7 Gaming Engine v0.9.6 - Stable Gaming Mode
# Target profile: Ryzen 5 3600 / RTX 2060 SUPER / 16 GB / Windows 10 19045 / 1080p 165 Hz
# No CPU Sets, EcoQoS, timer forcing, BCD/HPET tweaks, memory purges, or custom D7 power plans.

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

[System.Windows.Forms.Application]::EnableVisualStyles()

$Version = '0.9.6'
$AppRoot = Join-Path $env:ProgramData 'D7 Gaming Engine'
$LogDir = Join-Path $AppRoot 'GamingLogs'
New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
$LogPath = Join-Path $LogDir ("D7_Gaming_{0}.log" -f (Get-Date -Format 'yyyyMMdd_HHmmss'))

$script:SessionActive = $false
$script:GamePid = 0
$script:GameName = ''
$script:OriginalScheme = $null
$script:PriorityBackup = @{}
$script:GameModeBackup = @{}
$script:LowRamWarned = $false
$script:Exiting = $false
$script:GameNames = @('cod26-cod','cod','BlackOps6','ModernWarfare','ModernWarfareLauncher')

function Write-Log([string]$Text) {
    $line = "[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $Text
    Add-Content -Path $LogPath -Value $line -Encoding UTF8
    if ($script:LogBox -and -not $script:LogBox.IsDisposed) {
        $script:LogBox.AppendText($line + [Environment]::NewLine)
        $script:LogBox.SelectionStart = $script:LogBox.TextLength
        $script:LogBox.ScrollToCaret()
    }
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
        if (Test-Path $Path) {
            $obj = Get-ItemProperty -Path $Path -Name $Name -ErrorAction Stop
            $script:GameModeBackup[$key] = @{ Exists=$true; Value=$obj.$Name }
        } else {
            $script:GameModeBackup[$key] = @{ Exists=$false; Value=$null }
        }
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
    Write-Log 'Windows Game Mode: مفعّل خلال جلسة اللعب.'
}

function Restore-GameMode {
    foreach ($key in @($script:GameModeBackup.Keys)) {
        $parts = $key -split '\|', 2
        $path = $parts[0]; $name = $parts[1]
        $data = $script:GameModeBackup[$key]
        try {
            if ($data.Exists) {
                New-Item -Path $path -Force | Out-Null
                New-ItemProperty -Path $path -Name $name -PropertyType DWord -Value ([int]$data.Value) -Force | Out-Null
            } else {
                Remove-ItemProperty -Path $path -Name $name -Force -ErrorAction SilentlyContinue
            }
        } catch {}
    }
    $script:GameModeBackup = @{}
}

function Backup-AndSetPriority($Process,[System.Diagnostics.ProcessPriorityClass]$Priority) {
    try {
        if (-not $script:PriorityBackup.ContainsKey($Process.Id)) {
            $script:PriorityBackup[$Process.Id] = @{
                Name = $Process.ProcessName
                Priority = $Process.PriorityClass.ToString()
            }
        }
        if ($Process.PriorityClass -ne $Priority) {
            $Process.PriorityClass = $Priority
            Write-Log ("Priority: {0} PID={1} -> {2}" -f $Process.ProcessName,$Process.Id,$Priority)
        }
    } catch {}
}

function Restore-Priorities {
    foreach ($pidKey in @($script:PriorityBackup.Keys)) {
        try {
            $p = Get-Process -Id ([int]$pidKey) -ErrorAction Stop
            $old = [System.Diagnostics.ProcessPriorityClass][Enum]::Parse([System.Diagnostics.ProcessPriorityClass], [string]$script:PriorityBackup[$pidKey].Priority)
            $p.PriorityClass = $old
        } catch {}
    }
    $script:PriorityBackup = @{}
}

function Get-DetectedGame {
    foreach ($name in $script:GameNames) {
        $p = Get-Process -Name $name -ErrorAction SilentlyContinue | Sort-Object StartTime -Descending | Select-Object -First 1
        if ($p) { return $p }
    }
    try {
        $cims = Get-CimInstance Win32_Process | Where-Object {
            $_.ExecutablePath -and $_.ExecutablePath -match '(?i)\\Call of Duty\\' -and $_.Name -match '(?i)\.exe$'
        }
        foreach ($c in $cims) {
            if ($c.Name -notmatch '(?i)(launcher|crash|report)') {
                try { return Get-Process -Id $c.ProcessId -ErrorAction Stop } catch {}
            }
        }
    } catch {}
    return $null
}

function Tune-BackgroundForGaming {
    $free = Get-FreeRamGB
    $alwaysLow = @('fdm','Urban Vpn Updater','OneDriveStandaloneUpdater','AdobeARM','CCXProcess')
    foreach ($name in $alwaysLow) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
            Backup-AndSetPriority $_ ([System.Diagnostics.ProcessPriorityClass]::BelowNormal)
        }
    }
    if ($free -gt 0 -and $free -lt 4.0) {
        Get-Process -Name 'chrome' -ErrorAction SilentlyContinue | ForEach-Object {
            Backup-AndSetPriority $_ ([System.Diagnostics.ProcessPriorityClass]::BelowNormal)
        }
        Write-Log ("Smart RAM Guard: الذاكرة الحرة {0} GB؛ تم خفض أولوية Chrome فقط، بدون إغلاقه." -f $free)
    }
}

function Start-GameSession($Game) {
    if ($script:SessionActive) { return }
    $script:SessionActive = $true
    $script:GamePid = $Game.Id
    $script:GameName = $Game.ProcessName
    $script:LowRamWarned = $false
    Write-Log ("=== GAME SESSION START: {0} PID={1} ===" -f $Game.ProcessName,$Game.Id)
    $script:OriginalScheme = Get-ActivePowerScheme
    if ($script:OriginalScheme) { Write-Log ("Power plan before game: {0}" -f $script:OriginalScheme) }
    $highPerf = '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'
    if (Test-PowerScheme $highPerf) {
        if (Set-PowerScheme $highPerf) { Write-Log 'Power plan: Windows High performance مفعّل مؤقتًا.' }
    } else {
        Write-Log 'Power plan: High performance غير موجود؛ تم إبقاء خطة Windows الحالية.'
    }
    Set-GameModeForSession
    Backup-AndSetPriority $Game ([System.Diagnostics.ProcessPriorityClass]::AboveNormal)
    Tune-BackgroundForGaming
    if ($script:StatusLabel) {
        $script:StatusLabel.Text = "وضع اللعب نشط — $($Game.ProcessName)"
        $script:StatusLabel.ForeColor = [System.Drawing.Color]::LawnGreen
    }
}

function Maintain-GameSession {
    if (-not $script:SessionActive) { return }
    $game = Get-Process -Id $script:GamePid -ErrorAction SilentlyContinue
    if (-not $game) { Stop-GameSession; return }
    try {
        if ($game.PriorityClass -ne [System.Diagnostics.ProcessPriorityClass]::AboveNormal) {
            $game.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::AboveNormal
        }
    } catch {}
    $free = Get-FreeRamGB
    if ($free -gt 0 -and $free -lt 2.0 -and -not $script:LowRamWarned) {
        $script:LowRamWarned = $true
        Write-Log ("تحذير RAM: المتاح {0} GB. لن ننظف Standby List لأن ذلك قد يسبب تقطيعًا؛ خفّض Chrome/برامج الخلفية إذا استمر الضغط." -f $free)
        Tune-BackgroundForGaming
    }
}

function Stop-GameSession {
    if (-not $script:SessionActive) { return }
    Write-Log ("=== GAME SESSION END: {0} ===" -f $script:GameName)
    Restore-Priorities
    Restore-GameMode
    if ($script:OriginalScheme) {
        if (Set-PowerScheme $script:OriginalScheme) { Write-Log 'Power plan: تمت استعادة الخطة السابقة.' }
    }
    $script:SessionActive = $false
    $script:GamePid = 0
    $script:GameName = ''
    $script:OriginalScheme = $null
    if ($script:StatusLabel) {
        $script:StatusLabel.Text = 'المراقبة التلقائية جاهزة — بانتظار تشغيل اللعبة'
        $script:StatusLabel.ForeColor = [System.Drawing.Color]::DeepSkyBlue
    }
}

function Scan-System {
    Write-Log '--- فحص سريع للجهاز ---'
    try {
        $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
        Write-Log ("CPU: {0} | Cores={1} Threads={2}" -f $cpu.Name.Trim(),$cpu.NumberOfCores,$cpu.NumberOfLogicalProcessors)
    } catch {}
    try {
        $gpu = Get-CimInstance Win32_VideoController | Where-Object { $_.Name -match 'NVIDIA|AMD|Intel' } | Select-Object -First 1
        Write-Log ("GPU: {0} | VRAM≈{1:N1} GB" -f $gpu.Name,($gpu.AdapterRAM/1GB))
    } catch {}
    try {
        $cs = Get-CimInstance Win32_ComputerSystem
        Write-Log ("RAM: {0:N1} GB | Free={1} GB" -f ($cs.TotalPhysicalMemory/1GB),(Get-FreeRamGB))
    } catch {}
    $scheme = Get-ActivePowerScheme
    Write-Log ("Power plan GUID: {0}" -f $scheme)
    $game = Get-DetectedGame
    if ($game) { Write-Log ("Game detected: {0} PID={1}" -f $game.ProcessName,$game.Id) } else { Write-Log 'Game: غير مشغلة الآن.' }
    Write-Log 'Unsafe tweaks: CPU Sets / EcoQoS / Timer 1ms / BCD-HPET / RAM purge = OFF.'
}

function Show-CodSettings {
    $msg = @"
إعدادات COD المقترحة لجهازك — 1080p / 165Hz

Display:
• Fullscreen Exclusive إن توفر، أو Fullscreen Borderless إذا كان أكثر استقرارًا.
• Refresh Rate: 165Hz.
• V-Sync Gameplay: Off.
• NVIDIA Reflex: On + Boost.
• FPS Limit: 160 أثناء اللعب إذا VRR/G-SYNC شغال؛ ارفعها إذا تفضّل أعلى FPS.

Quality:
• Upscaling: DLSS Quality كبداية. إذا FPS أقل من المطلوب استخدم Balanced.
• Texture Resolution: Normal أو High (عندك 8GB VRAM).
• Texture Filter Anisotropic: High.
• On-Demand Texture Streaming: Off لتقليل تقطيع الشبكة/القرص.
• Shadow Quality / Screen Space Shadows: Low.
• Screen Space Reflections: Off.
• Volumetric Quality: Low.
• Particle Quality: Normal.
• Depth of Field / Motion Blur / Weapon Blur / Film Grain: Off.
• VRAM target: تقريبًا 70–80%، لا تدفعه إلى الحد الأقصى.

الفكرة: نحافظ على وضوح الصورة والـTextures، ونقص الإعدادات الثقيلة التي تضرب الـCPU/GPU وتسبب frame-time spikes.
"@
    [System.Windows.Forms.MessageBox]::Show($msg,'D7 — COD Recommended Settings',[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
}

function Enable-Startup {
    try {
        $exe = [Environment]::GetCommandLineArgs()[0]
        if ($exe -and (Test-Path $exe)) {
            $run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
            New-Item -Path $run -Force | Out-Null
            New-ItemProperty -Path $run -Name 'D7GamingEngine' -PropertyType String -Value ('"{0}" --background' -f $exe) -Force | Out-Null
            Write-Log 'Startup: تم تفعيل تشغيل D7 مع Windows مباشرة بدون PowerShell.'
            return $true
        }
    } catch {}
    Write-Log 'Startup: تعذر التفعيل.'
    return $false
}

function Disable-Startup {
    try {
        Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'D7GamingEngine' -Force -ErrorAction SilentlyContinue
        Write-Log 'Startup: تم إيقاف التشغيل التلقائي.'
        return $true
    } catch { return $false }
}

function Test-StartupEnabled {
    try {
        $v = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'D7GamingEngine' -ErrorAction Stop).D7GamingEngine
        return -not [string]::IsNullOrWhiteSpace($v)
    } catch { return $false }
}

$form = New-Object System.Windows.Forms.Form
$form.Text = "D7 Gaming Engine v$Version — Stable Gaming Mode"
$form.Size = New-Object System.Drawing.Size(980,680)
$form.StartPosition = 'CenterScreen'
$form.BackColor = [System.Drawing.Color]::FromArgb(12,16,20)
$form.ForeColor = [System.Drawing.Color]::White
$form.RightToLeft = 'Yes'
$form.RightToLeftLayout = $true
$form.Font = New-Object System.Drawing.Font('Segoe UI',10)

$title = New-Object System.Windows.Forms.Label
$title.Text = 'D7 STABLE GAMING MODE'
$title.Font = New-Object System.Drawing.Font('Segoe UI Semibold',22,[System.Drawing.FontStyle]::Bold)
$title.ForeColor = [System.Drawing.Color]::Chartreuse
$title.AutoSize = $true
$title.Location = New-Object System.Drawing.Point(610,28)
$form.Controls.Add($title)

$sub = New-Object System.Windows.Forms.Label
$sub.Text = 'Ryzen 5 3600 + RTX 2060 SUPER + 16GB — جلسة لعب تلقائية قابلة للاسترجاع'
$sub.AutoSize = $true
$sub.Location = New-Object System.Drawing.Point(430,75)
$sub.ForeColor = [System.Drawing.Color]::Silver
$form.Controls.Add($sub)

$status = New-Object System.Windows.Forms.Label
$status.Text = 'المراقبة التلقائية جاهزة — بانتظار تشغيل اللعبة'
$status.AutoSize = $true
$status.Location = New-Object System.Drawing.Point(610,108)
$status.ForeColor = [System.Drawing.Color]::DeepSkyBlue
$form.Controls.Add($status)
$script:StatusLabel = $status

$btnScan = New-Object System.Windows.Forms.Button
$btnScan.Text = 'فحص الجهاز الآن'
$btnScan.Size = New-Object System.Drawing.Size(190,48)
$btnScan.Location = New-Object System.Drawing.Point(745,145)
$form.Controls.Add($btnScan)

$btnForce = New-Object System.Windows.Forms.Button
$btnForce.Text = 'تفعيل وضع اللعب الآن'
$btnForce.Size = New-Object System.Drawing.Size(190,48)
$btnForce.Location = New-Object System.Drawing.Point(540,145)
$form.Controls.Add($btnForce)

$btnSettings = New-Object System.Windows.Forms.Button
$btnSettings.Text = 'إعدادات COD المقترحة'
$btnSettings.Size = New-Object System.Drawing.Size(190,48)
$btnSettings.Location = New-Object System.Drawing.Point(335,145)
$form.Controls.Add($btnSettings)

$btnRestore = New-Object System.Windows.Forms.Button
$btnRestore.Text = 'استرجاع الجلسة الآن'
$btnRestore.Size = New-Object System.Drawing.Size(190,48)
$btnRestore.Location = New-Object System.Drawing.Point(130,145)
$form.Controls.Add($btnRestore)

$chkStartup = New-Object System.Windows.Forms.CheckBox
$chkStartup.Text = 'تشغيل D7 تلقائيًا مع Windows (بدون PowerShell)'
$chkStartup.AutoSize = $true
$chkStartup.Location = New-Object System.Drawing.Point(590,210)
$chkStartup.Checked = Test-StartupEnabled
$form.Controls.Add($chkStartup)

$hint = New-Object System.Windows.Forms.Label
$hint.Text = 'لا نستخدم CPU affinity/Sets أو Timer 1ms أو تنظيف RAM القسري. نرفع أولوية اللعبة بشكل محافظ ونخفض برامج الخلفية فقط عند الحاجة.'
$hint.AutoSize = $false
$hint.Size = New-Object System.Drawing.Size(805,42)
$hint.Location = New-Object System.Drawing.Point(130,245)
$hint.ForeColor = [System.Drawing.Color]::DarkGray
$form.Controls.Add($hint)

$log = New-Object System.Windows.Forms.TextBox
$log.Multiline = $true
$log.ReadOnly = $true
$log.ScrollBars = 'Vertical'
$log.BackColor = [System.Drawing.Color]::FromArgb(8,12,16)
$log.ForeColor = [System.Drawing.Color]::Gainsboro
$log.Size = New-Object System.Drawing.Size(805,300)
$log.Location = New-Object System.Drawing.Point(130,300)
$log.RightToLeft = 'No'
$form.Controls.Add($log)
$script:LogBox = $log

$btnScan.Add_Click({ Scan-System })
$btnForce.Add_Click({
    $g = Get-DetectedGame
    if ($g) { Start-GameSession $g } else { Write-Log 'لم أجد COD تعمل الآن. شغل اللعبة وسيتم التفعيل تلقائيًا.' }
})
$btnSettings.Add_Click({ Show-CodSettings })
$btnRestore.Add_Click({ Stop-GameSession })
$chkStartup.Add_CheckedChanged({
    if ($chkStartup.Checked) { [void](Enable-Startup) } else { [void](Disable-Startup) }
})

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 2000
$timer.Add_Tick({
    if ($script:SessionActive) { Maintain-GameSession }
    else {
        $g = Get-DetectedGame
        if ($g) { Start-GameSession $g }
    }
})
$timer.Start()

$tray = New-Object System.Windows.Forms.NotifyIcon
$tray.Text = "D7 Gaming Engine v$Version"
$tray.Icon = [System.Drawing.SystemIcons]::Application
$tray.Visible = $true

$menu = New-Object System.Windows.Forms.ContextMenuStrip
$itemShow = $menu.Items.Add('فتح D7')
$itemExit = $menu.Items.Add('خروج واسترجاع الإعدادات')
$tray.ContextMenuStrip = $menu
$itemShow.Add_Click({ $form.Show(); $form.WindowState='Normal'; $form.Activate() })
$itemExit.Add_Click({
    $script:Exiting = $true
    Stop-GameSession
    $timer.Stop()
    $tray.Visible = $false
    $form.Close()
})
$tray.Add_DoubleClick({ $form.Show(); $form.WindowState='Normal'; $form.Activate() })

$form.Add_FormClosing({
    param($sender,$e)
    if (-not $script:Exiting) {
        $e.Cancel = $true
        $form.Hide()
        $tray.ShowBalloonTip(1500,'D7 Gaming Engine','D7 مستمر بالخلفية لمراقبة تشغيل اللعبة.','Info')
    }
})

$form.Add_Shown({
    Write-Log "D7 Gaming Engine v$Version started."
    Write-Log 'Auto Watch: cod26-cod.exe + Call of Duty process family.'
    Write-Log 'Safe policy: reversible session only; no aggressive legacy tweaks.'
    if (-not (Test-StartupEnabled)) {
        if (Enable-Startup) { $chkStartup.Checked = $true }
    }
    Scan-System
    $g = Get-DetectedGame
    if ($g) { Start-GameSession $g }
    $args = [Environment]::GetCommandLineArgs()
    if ($args -contains '--background') { $form.Hide() }
})

[void]$form.ShowDialog()
if ($script:SessionActive) { Stop-GameSession }
$tray.Visible = $false
