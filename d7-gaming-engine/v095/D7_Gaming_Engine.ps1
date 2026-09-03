Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$script:Version='0.9.5'
$script:StableUrl='https://raw.githubusercontent.com/Emperor0/em/main/d7-gaming-engine/stable/latest.json'
$script:AppDir=Split-Path -Parent ([Diagnostics.Process]::GetCurrentProcess().MainModule.FileName)
$script:DataDir=Join-Path $env:ProgramData 'D7 Gaming Engine'
$script:LogDir=Join-Path $script:DataDir 'Logs'
New-Item -ItemType Directory -Path $script:LogDir -Force | Out-Null
$script:LastReport=$null

function LogLine([string]$Text,[string]$Level='INFO'){
    $ts=Get-Date -Format 'HH:mm:ss'
    $line="[$ts][$Level] $Text"
    if($script:LogBox){
        $script:LogBox.AppendText($line+"`r`n")
        $script:LogBox.SelectionStart=$script:LogBox.Text.Length
        $script:LogBox.ScrollToCaret()
        [System.Windows.Forms.Application]::DoEvents()
    }
}
function SaveReport([string[]]$Lines,[string]$Prefix){
    $p=Join-Path $script:LogDir ("{0}_{1}.txt" -f $Prefix,(Get-Date -Format 'yyyyMMdd_HHmmss'))
    [IO.File]::WriteAllLines($p,$Lines,(New-Object Text.UTF8Encoding($true)))
    $script:LastReport=$p
    LogLine "تم حفظ التقرير: $p" 'OK'
    return $p
}
function Get-ActivePowerPlan {
    $out=& powercfg /getactivescheme 2>&1
    return ($out -join ' ')
}
function Get-LegacyD7Tasks {
    $exact=@('D7 Auto Performance Profiles','D7 Gaming OS 2.0','D7 Gaming OS FINAL','D7 Performance Governor','D7 Ryzen Master Profile 2 Auto Apply','D7 Total Auto Optimizer 4.0')
    $hits=@()
    Get-ScheduledTask -ErrorAction SilentlyContinue | ForEach-Object {
        $t=$_
        $action=(@($t.Actions)|ForEach-Object{"$($_.Execute) $($_.Arguments) $($_.WorkingDirectory)"}) -join ' '
        if($exact -contains $t.TaskName -or $action -match '(?i)\\ProgramData\\D7GamingOS\\|\\ProgramData\\D7PerformanceGovernor\\'){
            $hits += [pscustomobject]@{Task=$t;Action=$action}
        }
    }
    return $hits
}
function Invoke-SelfHeal {
    LogLine 'بدء Self-Heal لبقايا D7 القديمة...'
    $backup=Join-Path $script:DataDir ("LegacyBackup_"+(Get-Date -Format 'yyyyMMdd_HHmmss'))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $count=0
    foreach($h in @(Get-LegacyD7Tasks)){
        try{
            $xml=Export-ScheduledTask -TaskName $h.Task.TaskName -TaskPath $h.Task.TaskPath
            [IO.File]::WriteAllText((Join-Path $backup (($h.Task.TaskName -replace '[^\w\- ]','_')+'.xml')),$xml,(New-Object Text.UTF8Encoding($true)))
        }catch{}
        try{
            Stop-ScheduledTask -TaskName $h.Task.TaskName -TaskPath $h.Task.TaskPath -ErrorAction SilentlyContinue
            Unregister-ScheduledTask -TaskName $h.Task.TaskName -TaskPath $h.Task.TaskPath -Confirm:$false -ErrorAction Stop
            LogLine ("تم حذف مهمة D7 قديمة: "+$h.Task.TaskName) 'OK'; $count++
        }catch{ LogLine ("تعذر حذف مهمة: "+$h.Task.TaskName) 'WARN' }
    }
    foreach($d in @("$env:ProgramData\D7GamingOS","$env:ProgramData\D7PerformanceGovernor")){
        if(Test-Path $d){
            try{
                $dest=Join-Path $backup (Split-Path $d -Leaf)
                Move-Item $d $dest -Force -ErrorAction Stop
                LogLine ("تم عزل مجلد قديم: "+$d) 'OK'
            }catch{ LogLine ("تعذر عزل: "+$d) 'WARN' }
        }
    }
    & powercfg /setactive SCHEME_BALANCED | Out-Null
    LogLine ("Self-Heal انتهى. العناصر المحذوفة: "+$count) 'OK'
}
function QuickScan {
    LogLine 'بدء الفحص السريع...'
    $r=New-Object System.Collections.Generic.List[string]
    $r.Add("D7 Gaming Engine v$script:Version - Quick Scan")
    $r.Add("Time: $(Get-Date)")
    try{
        $os=Get-CimInstance Win32_OperatingSystem
        $cpu=Get-CimInstance Win32_Processor | Select-Object -First 1
        $gpu=Get-CimInstance Win32_VideoController | Sort-Object AdapterRAM -Descending | Select-Object -First 1
        $ram=[math]::Round($os.TotalVisibleMemorySize/1MB,1)
        $free=[math]::Round($os.FreePhysicalMemory/1MB,1)
        $r.Add("OS: $($os.Caption) build $($os.BuildNumber)")
        $r.Add("CPU: $($cpu.Name)")
        $r.Add("GPU: $($gpu.Name)")
        $r.Add("RAM: $ram GB total / $free GB free")
        LogLine "CPU: $($cpu.Name)"
        LogLine "GPU: $($gpu.Name)"
        LogLine "RAM المتاح: $free GB"
        if($free -lt 3.5){ LogLine 'الرام المتاح منخفض وقد يسبب تقطيع بالألعاب.' 'WARN'; $r.Add('WARN: Low free RAM') }
    }catch{ LogLine 'تعذر قراءة معلومات العتاد.' 'WARN' }

    $plan=Get-ActivePowerPlan
    $r.Add("Power: $plan"); LogLine ("خطة الطاقة: "+$plan)

    $legacy=@(Get-LegacyD7Tasks)
    $r.Add("Legacy D7 tasks: "+$legacy.Count)
    if($legacy.Count){LogLine ("وجدت مهام D7 قديمة: "+$legacy.Count) 'WARN'}else{LogLine 'لا توجد مهام D7 قديمة.' 'OK'}

    try{
        $bad=Get-CimInstance Win32_PnPEntity | Where-Object {$_.ConfigManagerErrorCode -ne 0}
        foreach($d in $bad){
            if($d.ConfigManagerErrorCode -eq 22){
                $r.Add("DEVICE DISABLED: $($d.Name)")
                LogLine ("جهاز Disabled: "+$d.Name) 'INFO'
            }else{
                $r.Add("DEVICE ERROR $($d.ConfigManagerErrorCode): $($d.Name)")
                LogLine ("مشكلة جهاز Code $($d.ConfigManagerErrorCode): "+$d.Name) 'WARN'
            }
        }
    }catch{}

    try{
        $c=(Get-PSDrive C).Free/1GB
        $r.Add(("C free: {0:N1} GB" -f $c))
        LogLine (("مساحة C المتاحة: {0:N1} GB" -f $c))
    }catch{}

    try{
        $ev=Get-WinEvent -FilterHashtable @{LogName='System';Id=41,6008;StartTime=(Get-Date).AddDays(-3)} -ErrorAction SilentlyContinue
        $r.Add("Recent shutdown/kernel events: "+@($ev).Count)
        if(@($ev).Count -gt 2){LogLine ("أحداث إغلاق/Kernel حديثة: "+@($ev).Count) 'WARN'}
    }catch{}
    SaveReport $r 'D7_QUICK_SCAN' | Out-Null
    LogLine 'الفحص السريع اكتمل.' 'OK'
}
function SafeGamePrep {
    LogLine 'بدء تحضير اللعب الآمن...'
    try{
        New-Item 'HKCU:\Software\Microsoft\GameBar' -Force | Out-Null
        New-ItemProperty 'HKCU:\Software\Microsoft\GameBar' -Name AutoGameModeEnabled -PropertyType DWord -Value 1 -Force | Out-Null
        New-ItemProperty 'HKCU:\Software\Microsoft\GameBar' -Name AllowAutoGameMode -PropertyType DWord -Value 1 -Force | Out-Null
        LogLine 'تم تفعيل Windows Game Mode.' 'OK'
    }catch{LogLine 'تعذر ضبط Game Mode.' 'WARN'}
    & powercfg /setactive SCHEME_BALANCED | Out-Null
    LogLine 'تم تثبيت Windows Balanced لمنع تعارض خطط D7 القديمة.' 'OK'
    try{
        $os=Get-CimInstance Win32_OperatingSystem
        $free=[math]::Round($os.FreePhysicalMemory/1MB,1)
        if($free -lt 3.5){LogLine "تحذير: المتاح من RAM فقط $free GB." 'WARN'}else{LogLine "RAM المتاح $free GB." 'OK'}
    }catch{}
    LogLine 'لم يتم تطبيق CPU Sets أو EcoQoS أو Timer 1ms أو Priority Boost.' 'INFO'
}
function Start-FullRepair {
    if($script:RepairProcess -and -not $script:RepairProcess.HasExited){ LogLine 'الإصلاح الشامل يعمل بالفعل.' 'WARN'; return }
    $rep=Join-Path $script:LogDir ("D7_FULL_REPAIR_"+(Get-Date -Format 'yyyyMMdd_HHmmss')+".txt")
    $helper=Join-Path $env:TEMP ("D7_FullRepair_"+[guid]::NewGuid().ToString('N')+".ps1")
    $code=@'
param([string]$Report)
function L($s){Add-Content -Path $Report -Value ("[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'),$s) -Encoding UTF8}
L "START"
$d=Start-Process dism.exe -ArgumentList "/Online /Cleanup-Image /RestoreHealth" -WindowStyle Hidden -Wait -PassThru
L ("DISM ExitCode="+$d.ExitCode)
$s=Start-Process sfc.exe -ArgumentList "/scannow" -WindowStyle Hidden -Wait -PassThru
L ("SFC ExitCode="+$s.ExitCode)
$c=Start-Process chkdsk.exe -ArgumentList "C: /scan" -WindowStyle Hidden -Wait -PassThru
L ("CHKDSK ExitCode="+$c.ExitCode)
L "DONE"
'@
    [IO.File]::WriteAllText($helper,$code,(New-Object Text.UTF8Encoding($true)))
    $script:RepairReport=$rep
    $script:RepairRead=0
    $script:RepairProcess=Start-Process powershell.exe -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-WindowStyle','Hidden','-File',"`"$helper`"","-Report", "`"$rep`"") -WindowStyle Hidden -PassThru
    $script:RepairTimer.Start()
    LogLine 'بدأ الإصلاح الشامل بالخلفية: DISM ثم SFC ثم CHKDSK.' 'OK'
}
function CheckUpdates {
    LogLine 'جاري فحص التحديثات...'
    try{
        $m=Invoke-RestMethod -Uri $script:StableUrl -UseBasicParsing -TimeoutSec 15
        if([version]$m.version -le [version]$script:Version){LogLine "أنت على آخر إصدار: $script:Version" 'OK'; return}
        LogLine ("وجد تحديث: "+$m.version) 'OK'
        $ans=[Windows.Forms.MessageBox]::Show("يوجد تحديث $($m.version). تثبيت الآن؟",'D7 Update',[Windows.Forms.MessageBoxButtons]::YesNo,[Windows.Forms.MessageBoxIcon]::Question)
        if($ans -ne [Windows.Forms.DialogResult]::Yes){return}
        $zip=Join-Path $env:TEMP ("D7_Update_"+$m.version+".zip")
        Invoke-WebRequest -Uri $m.packageUrl -OutFile $zip -UseBasicParsing
        $sha=(Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        if($sha -ne ([string]$m.sha256).ToLowerInvariant()){throw 'SHA256 mismatch'}
        $dest=Join-Path $env:TEMP ("D7_Update_"+[guid]::NewGuid().ToString('N'))
        Expand-Archive $zip $dest -Force
        $exe=Get-ChildItem $dest -Recurse -Filter D7_Gaming_Engine.exe | Select-Object -First 1
        if(-not $exe){throw 'EXE missing'}
        $up=Join-Path $env:TEMP ("D7_Updater_"+[guid]::NewGuid().ToString('N')+".ps1")
        $target=$script:AppDir
        $pid0=$PID
        $uc=@"
Start-Sleep -Seconds 2
try { Wait-Process -Id $pid0 -Timeout 20 -ErrorAction SilentlyContinue } catch {}
Copy-Item -Path '$($exe.Directory.FullName)\*' -Destination '$target' -Recurse -Force
Start-Process -FilePath '$target\D7_Gaming_Engine.exe'
"@
        [IO.File]::WriteAllText($up,$uc,(New-Object Text.UTF8Encoding($true)))
        Start-Process powershell.exe -ArgumentList "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$up`"" -WindowStyle Hidden
        $script:Form.Close()
    }catch{LogLine ("فشل التحديث: "+$_.Exception.Message) 'ERROR'}
}

$form=New-Object Windows.Forms.Form
$script:Form=$form
$form.Text="D7 Gaming Engine v$script:Version — Functional Core"
$form.Size=New-Object Drawing.Size(1100,720)
$form.StartPosition='CenterScreen'
$form.BackColor=[Drawing.Color]::FromArgb(10,14,18)
$form.ForeColor=[Drawing.Color]::White
$form.RightToLeft=[Windows.Forms.RightToLeft]::Yes
$form.RightToLeftLayout=$true
$form.Font=New-Object Drawing.Font('Segoe UI',10)

$title=New-Object Windows.Forms.Label
$title.Text="D7 GAMING ENGINE  v$script:Version"
$title.ForeColor=[Drawing.Color]::Chartreuse
$title.Font=New-Object Drawing.Font('Segoe UI',22,[Drawing.FontStyle]::Bold)
$title.Location=New-Object Drawing.Point(40,25); $title.AutoSize=$true
$form.Controls.Add($title)

$sub=New-Object Windows.Forms.Label
$sub.Text='نسخة تنفيذية: كل زر ينفذ فعليًا ويعرض النتيجة مباشرة.'
$sub.Location=New-Object Drawing.Point(42,72); $sub.AutoSize=$true
$form.Controls.Add($sub)

function AddBtn($text,$x,$handler){
    $b=New-Object Windows.Forms.Button
    $b.Text=$text; $b.Location=New-Object Drawing.Point($x,115); $b.Size=New-Object Drawing.Size(190,48)
    $b.FlatStyle='Flat'; $b.BackColor=[Drawing.Color]::FromArgb(32,40,50); $b.ForeColor=[Drawing.Color]::White
    $b.Add_Click($handler); $form.Controls.Add($b); return $b
}
$b1=AddBtn 'فحص سريع فعلي' 40 {QuickScan}
$b2=AddBtn 'Self-Heal لبقايا D7' 245 {Invoke-SelfHeal}
$b3=AddBtn 'تحضير اللعب الآمن' 450 {SafeGamePrep}
$b4=AddBtn 'إصلاح شامل' 655 {Start-FullRepair}
$b5=AddBtn 'التحديثات أونلاين' 860 {CheckUpdates}

$open=New-Object Windows.Forms.Button
$open.Text='فتح آخر تقرير'; $open.Location=New-Object Drawing.Point(860,175); $open.Size=New-Object Drawing.Size(190,38)
$open.Add_Click({if($script:LastReport -and (Test-Path $script:LastReport)){Start-Process notepad.exe $script:LastReport}else{LogLine 'لا يوجد تقرير بعد.' 'WARN'}})
$form.Controls.Add($open)

$log=New-Object Windows.Forms.RichTextBox
$script:LogBox=$log
$log.Location=New-Object Drawing.Point(40,230); $log.Size=New-Object Drawing.Size(1010,400)
$log.BackColor=[Drawing.Color]::FromArgb(15,20,26); $log.ForeColor=[Drawing.Color]::Gainsboro
$log.ReadOnly=$true; $log.Font=New-Object Drawing.Font('Consolas',10)
$form.Controls.Add($log)

$status=New-Object Windows.Forms.Label
$status.Text='جاهز'; $status.Location=New-Object Drawing.Point(40,645); $status.AutoSize=$true; $status.ForeColor=[Drawing.Color]::LightGreen
$form.Controls.Add($status)

$script:RepairTimer=New-Object Windows.Forms.Timer
$script:RepairTimer.Interval=1500
$script:RepairTimer.Add_Tick({
    if($script:RepairReport -and (Test-Path $script:RepairReport)){
        $lines=Get-Content $script:RepairReport
        if($lines.Count -gt $script:RepairRead){
            $lines[$script:RepairRead..($lines.Count-1)] | ForEach-Object { LogLine $_ 'REPAIR' }
            $script:RepairRead=$lines.Count
        }
    }
    if($script:RepairProcess -and $script:RepairProcess.HasExited){
        $script:RepairTimer.Stop()
        $script:LastReport=$script:RepairReport
        LogLine 'الإصلاح الشامل انتهى. افتح التقرير لمراجعة Exit Codes.' 'OK'
    }
})

$form.Add_Shown({
    LogLine "D7 v$script:Version جاهز." 'OK'
    LogLine 'تشغيل Self-Heal سريع عند البدء...'
    Invoke-SelfHeal
    LogLine 'اضغط «فحص سريع فعلي» لرؤية حالة الجهاز الآن.'
})
[void]$form.ShowDialog()
