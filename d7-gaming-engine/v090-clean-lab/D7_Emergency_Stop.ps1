# D7 Clean Lab Emergency Stop
$ErrorActionPreference='SilentlyContinue'
Add-Type -AssemblyName System.Windows.Forms

Get-Process -Name 'D7_Gaming_Engine_Core_v0.9.0','D7_Gaming_Engine','PresentMon','PresentMon-2.5.1-x64' -ErrorAction SilentlyContinue | ForEach-Object {
  try { Stop-Process -Id $_.Id -Force } catch {}
}

try { powercfg /setactive SCHEME_BALANCED | Out-Null } catch {}
$plans=(powercfg /list | Out-String) -split "`r?`n"
foreach($line in $plans){
  if($line -match '(?i)D7 GAME PERFORMANCE' -and $line -match '([0-9a-fA-F-]{36})'){
    try { powercfg /setactive SCHEME_BALANCED | Out-Null; powercfg /delete $matches[1] | Out-Null } catch {}
  }
}

[System.Windows.Forms.MessageBox]::Show('تم إيقاف D7 v0.9.0 وPresentMon وإرجاع Windows Balanced وحذف خطة D7 GAME PERFORMANCE إن وجدت. أعد تشغيل الجهاز إذا بقي أي ثقل.','D7 Emergency Stop','OK','Information') | Out-Null
