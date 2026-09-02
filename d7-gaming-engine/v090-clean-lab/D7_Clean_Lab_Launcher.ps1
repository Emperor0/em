# D7 Gaming Engine v0.9.0 Clean Lab Launcher
# Runs the original v0.9.0 core in a controlled clean session.
# No startup registration, no scheduled task creation, no services.

$ErrorActionPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.Windows.Forms

$base = Split-Path -Parent $MyInvocation.MyCommand.Path
$core = Join-Path $base 'D7_Gaming_Engine_Core_v0.9.0.exe'
$logDir = Join-Path $env:ProgramData 'D7 Gaming Engine\CleanLab'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$log = Join-Path $logDir "CleanLab_$stamp.log"

function Log([string]$m) {
  $line = "[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $m
  Add-Content -Path $log -Value $line -Encoding UTF8
}

function Stop-LegacyD7Processes {
  Get-CimInstance Win32_Process | Where-Object {
    $_.CommandLine -and ($_.CommandLine -match '(?i)\\ProgramData\\(D7GamingOS|D7PerformanceGovernor)\\' -or $_.CommandLine -match '(?i)D7_TOTAL_AUTO_OPTIMIZER|D7_RyzenMaster_Profile2|D7_Performance_Governor_v1|D7GamingOS')
  } | ForEach-Object {
    try { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop; Log "Stopped legacy D7 PID=$($_.ProcessId) $($_.Name)" } catch {}
  }
}

function Disable-LegacyD7Tasks {
  $exact = @(
    'D7 Auto Performance Profiles',
    'D7 Gaming OS 2.0',
    'D7 Gaming OS FINAL',
    'D7 Performance Governor',
    'D7 Ryzen Master Profile 2 Auto Apply',
    'D7 Total Auto Optimizer 4.0'
  )
  Get-ScheduledTask | ForEach-Object {
    $t = $_
    $actions = (@($t.Actions) | ForEach-Object { "$($_.Execute) $($_.Arguments) $($_.WorkingDirectory)" }) -join ' '
    if (($exact -contains $t.TaskName) -or $actions -match '(?i)\\ProgramData\\(D7GamingOS|D7PerformanceGovernor)\\') {
      try { Disable-ScheduledTask -TaskName $t.TaskName -TaskPath $t.TaskPath | Out-Null; Log "Disabled legacy task: $($t.TaskPath)$($t.TaskName)" } catch {}
    }
  }
}

function Restore-CleanBaseline {
  try { powercfg /setactive SCHEME_BALANCED | Out-Null; Log 'Windows Balanced activated.' } catch {}
  $plans = (powercfg /list | Out-String) -split "`r?`n"
  foreach ($line in $plans) {
    if ($line -match '(?i)D7 GAME PERFORMANCE' -and $line -match '([0-9a-fA-F-]{36})') {
      $guid = $matches[1]
      try { powercfg /setactive SCHEME_BALANCED | Out-Null; powercfg /delete $guid | Out-Null; Log "Removed stale D7 GAME PERFORMANCE plan: $guid" } catch {}
    }
  }
}

function Preflight {
  Log '=== Clean Lab preflight ==='
  Stop-LegacyD7Processes
  Disable-LegacyD7Tasks
  Restore-CleanBaseline

  $remaining = @()
  Get-ScheduledTask | ForEach-Object {
    $t=$_; $a=(@($t.Actions)|ForEach-Object{"$($_.Execute) $($_.Arguments)"}) -join ' '
    if ($t.State -ne 'Disabled' -and $a -match '(?i)\\ProgramData\\(D7GamingOS|D7PerformanceGovernor)\\') { $remaining += $t.TaskName }
  }
  if ($remaining.Count -gt 0) {
    Log ('BLOCKED: active legacy tasks remain: ' + ($remaining -join ', '))
    [System.Windows.Forms.MessageBox]::Show("ما قدرت أعزل كل مهام D7 القديمة. لن أشغل الاختبار حتى لا تختلط النتائج.`n`nراجع السجل:`n$log",'D7 Clean Lab','OK','Error') | Out-Null
    return $false
  }
  return $true
}

function FinalRollback {
  Log '=== Clean Lab rollback ==='
  Get-Process -Name 'PresentMon','PresentMon-2.5.1-x64' -ErrorAction SilentlyContinue | ForEach-Object {
    try { Stop-Process -Id $_.Id -Force } catch {}
  }
  Restore-CleanBaseline
  Log 'Rollback complete. No startup/task/service was created by Clean Lab.'
}

if (-not (Test-Path $core)) {
  [System.Windows.Forms.MessageBox]::Show("ملف قلب v0.9.0 غير موجود:`n$core",'D7 Clean Lab','OK','Error') | Out-Null
  exit 2
}

if (-not (Preflight)) { exit 3 }

$msg = @"
D7 v0.9.0 Clean Lab جاهز.

• تم تعطيل بقايا D7 القديمة لهذه التجربة.
• تم بدء الاختبار من Windows Balanced.
• قلب الأداء هو v0.9.0 الأصلي.
• لا يتم إنشاء Startup أو Scheduled Tasks أو Services.
• بعد إغلاق D7 سيتم تنفيذ Rollback تلقائياً.
• لو حصل ثقل استخدم D7_Emergency_Stop.exe فوراً.

ابدأ اللعبة من داخل الجلسة وراقب الأداء.
"@
[System.Windows.Forms.MessageBox]::Show($msg,'D7 v0.9.0 Clean Lab','OK','Information') | Out-Null

try {
  Log "Launching original core: $core"
  $p = Start-Process -FilePath $core -WorkingDirectory $base -PassThru
  if (-not $p) { throw 'Core did not start.' }
  Log "Core PID=$($p.Id)"

  # Session watchdog: if Explorer crashes repeatedly during this Clean Lab session, stop D7 and rollback.
  $sessionStart = Get-Date
  $lastExplorerFaultCount = 0
  while (-not $p.HasExited) {
    Start-Sleep -Seconds 3
    try { $p.Refresh() } catch { break }

    $faults = @(Get-WinEvent -FilterHashtable @{LogName='Application'; Id=1000; StartTime=$sessionStart} -ErrorAction SilentlyContinue | Where-Object { $_.Message -match '(?i)explorer\.exe' })
    if ($faults.Count -ge 2 -and $faults.Count -gt $lastExplorerFaultCount) {
      Log "WATCHDOG: repeated Explorer faults detected ($($faults.Count)). Stopping core."
      try { Stop-Process -Id $p.Id -Force } catch {}
      [System.Windows.Forms.MessageBox]::Show('D7 Clean Lab أوقف v0.9.0 تلقائياً لأن Explorer سجل أعطال متكررة أثناء الجلسة. تم تنفيذ Rollback.','D7 Watchdog','OK','Warning') | Out-Null
      break
    }
    $lastExplorerFaultCount = $faults.Count
  }
} catch {
  Log "Launch/runtime error: $($_.Exception.Message)"
} finally {
  FinalRollback
}

[System.Windows.Forms.MessageBox]::Show("انتهت جلسة Clean Lab وتم إرجاع Windows Balanced وإزالة أي خطة D7 مؤقتة.`n`nالسجل:`n$log",'D7 Clean Lab','OK','Information') | Out-Null
