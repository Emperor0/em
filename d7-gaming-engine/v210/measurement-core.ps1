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

function Find-ExeDeep {
    param([string]$Name,[string[]]$KnownPaths,[string[]]$SearchRoots)
    foreach ($p in $KnownPaths) {
        if ($p -and (Test-Path $p)) { return (Resolve-Path $p).Path }
    }
    try {
        $cmd = Get-Command $Name -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($cmd -and $cmd.Source -and (Test-Path $cmd.Source)) { return $cmd.Source }
    } catch {}
    foreach ($root in $SearchRoots) {
        if (-not $root -or -not (Test-Path $root)) { continue }
        try {
            $hit = Get-ChildItem -Path $root -Filter $Name -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($hit) { return $hit.FullName }
        } catch {}
    }
    return $null
}

function ToolInventory {
    $pf=$env:ProgramFiles; $pf86=${env:ProgramFiles(x86)}; $la=$env:LOCALAPPDATA; $pd=$env:ProgramData
    $apps = Get-InstalledAppIndex
    $wingetRoot = Join-Path $la 'Microsoft\WinGet\Packages'
    $taskNames = @(); try { $taskNames = @(Get-ScheduledTask -ErrorAction SilentlyContinue | ForEach-Object TaskName) } catch {}

    $defs = @(
        @{N='MSI Afterburner'; Exe='MSIAfterburner.exe'; Paths=@("$pf86\MSI Afterburner\MSIAfterburner.exe","$pf\MSI Afterburner\MSIAfterburner.exe"); Roots=@($pf86,$pf); Match='MSI Afterburner'},
        @{N='RTSS'; Exe='RTSS.exe'; Paths=@("$pf86\RivaTuner Statistics Server\RTSS.exe","$pf\RivaTuner Statistics Server\RTSS.exe"); Roots=@($pf86,$pf); Match='RivaTuner Statistics Server'},
        @{N='ISLC'; Exe='Intelligent standby list cleaner ISLC.exe'; Paths=@("$pf\ISLC\Intelligent standby list cleaner ISLC.exe","$pf86\ISLC\Intelligent standby list cleaner ISLC.exe"); Roots=@($pf,$pf86,$la); Match='Intelligent Standby'},
        @{N='LatencyMon'; Exe='LatMon.exe'; Paths=@("$pf\LatencyMon\LatMon.exe","$pf86\LatencyMon\LatMon.exe"); Roots=@($pf,$pf86); Match='LatencyMon'},
        @{N='Fan Control'; Exe='FanControl.exe'; Paths=@("$la\Programs\FanControl\FanControl.exe","$pf\Fan Control\FanControl.exe","$pd\FanControl\FanControl.exe"); Roots=@("$la\Programs",$pf,$pd); Match='Fan Control'},
        @{N='System Informer'; Exe='SystemInformer.exe'; Paths=@("$pf\SystemInformer\SystemInformer.exe","$pf86\SystemInformer\SystemInformer.exe"); Roots=@($pf,$pf86,$la); Match='System Informer'},
        @{N='PresentMon'; Exe='PresentMon.exe'; Paths=@("$pf\Intel\PresentMon\PresentMon.exe","$pf\PresentMon\PresentMon.exe","$ToolRoot\PresentMon\PresentMon.exe"); Roots=@($wingetRoot,$ToolRoot,$pf); Match='PresentMon'},
        @{N='NVIDIA Profile Inspector'; Exe='nvidiaProfileInspector.exe'; Paths=@("$ToolRoot\nvidiaProfileInspector.exe","$pf\NVIDIA Profile Inspector\nvidiaProfileInspector.exe"); Roots=@($ToolRoot,$pf,$pf86); Match='NVIDIA Profile Inspector'},
        @{N='DDU'; Exe='Display Driver Uninstaller.exe'; Paths=@("$ToolRoot\DDU\Display Driver Uninstaller.exe"); Roots=@($ToolRoot); Match='Display Driver Uninstaller'},
        @{N='OCCT'; Exe='OCCT.exe'; Paths=@("$pf\OCCT\OCCT.exe","$pf86\OCCT\OCCT.exe"); Roots=@($pf,$pf86,$wingetRoot); Match='OCCT'},
        @{N='ZenTimings'; Exe='ZenTimings.exe'; Paths=@("$ToolRoot\ZenTimings\ZenTimings.exe"); Roots=@($ToolRoot,$la,$pf,$pf86); Match='ZenTimings'}
    )

    $out = @()
    foreach ($d in $defs) {
        $path = Find-ExeDeep -Name $d.Exe -KnownPaths $d.Paths -SearchRoots $d.Roots
        $app = $apps | Where-Object { $_.DisplayName -like ('*'+$d.Match+'*') } | Select-Object -First 1
        $task = $taskNames | Where-Object { $_ -like ('*'+$d.Match+'*') -or $_ -like ('*'+$d.N+'*') } | Select-Object -First 1
        $installed = [bool]($path -or $app -or $task)
        $ver = $null
        if ($path) { try { $ver = FileVersion $path } catch {} }
        if (-not $ver -and $app) { $ver = $app.DisplayVersion }
        $out += [pscustomobject]@{Name=$d.N;Installed=$installed;Path=$path;Version=$ver;DetectedBy=$(if($path){'Executable'}elseif($app){'InstalledApps'}elseif($task){'ScheduledTask'}else{'None'})}
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
        GPU_Util=$(if($gpu){$gpu.Util}else{$null});GPU_TempC=$(if($gpu){$gpu.Temp_C}else{$null});GPU_PowerW=$(if($gpu){$gpu.Power_W}else{$null});GPU_ClockMHz=$(if($gpu){$gpu.Clock_MHz}else{$null})
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
        foreach($d in $devs){
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
        foreach($a in @(Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object Status -eq 'Up')){
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
    if($x){ return [pscustomobject]@{Available=$true;Path=$x.Source} }
    return [pscustomobject]@{Available=$false;Path=$null}
}

function Invoke-PresentMonCapture {
    param([int]$ProcessId,[int]$Seconds=20,[string]$OutputCsv)
    $pm=Get-PresentMonPath
    if(-not $pm){ return [pscustomobject]@{Available=$false;Success=$false;Reason='PresentMon not detected';Csv=$null} }
    try {
        $help=(& $pm --help 2>&1 | Out-String)
        if($help -match '--process_id'){
            & $pm --process_id $ProcessId --timed $Seconds --output_file $OutputCsv 2>$null | Out-Null
        } else {
            & $pm -process_id $ProcessId -timed $Seconds -output_file $OutputCsv 2>$null | Out-Null
        }
        if(Test-Path $OutputCsv){ return [pscustomobject]@{Available=$true;Success=$true;Reason=$null;Csv=$OutputCsv} }
        return [pscustomobject]@{Available=$true;Success=$false;Reason='No CSV produced';Csv=$null}
    } catch {
        return [pscustomobject]@{Available=$true;Success=$false;Reason=$_.Exception.Message;Csv=$null}
    }
}

function Get-Percentile {
    param([double[]]$Values,[double]$P)
    if(-not $Values -or $Values.Count -eq 0){ return $null }
    $s=@($Values | Sort-Object)
    $i=[math]::Ceiling(($P/100.0)*$s.Count)-1
    if($i -lt 0){$i=0}; if($i -ge $s.Count){$i=$s.Count-1}
    return [double]$s[$i]
}

function Analyze-PresentMonCsv {
    param([string]$Csv)
    if(-not(Test-Path $Csv)){ return $null }
    try { $rows=@(Import-Csv $Csv) } catch { return $null }
    if($rows.Count -lt 10){ return [pscustomobject]@{Frames=$rows.Count;Valid=$false} }
    $props=@($rows[0].PSObject.Properties.Name)
    $candidates=@('MsBetweenPresents','CPUFrameTime','FrameTime','DisplayedTime','MsUntilRenderComplete')
    $col=$candidates | Where-Object { $props -contains $_ } | Select-Object -First 1
    if(-not $col){ return [pscustomobject]@{Frames=$rows.Count;Valid=$false;Columns=$props} }
    $ft=@()
    foreach($r in $rows){
        $v=0.0
        if([double]::TryParse([string]$r.$col,[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$v)){
            if($v -gt 0 -and $v -lt 1000){$ft+=$v}
        }
    }
    if($ft.Count -lt 10){ return [pscustomobject]@{Frames=$rows.Count;Valid=$false;FrameTimeColumn=$col} }
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
    $samples=@();1..10|ForEach-Object{$samples+=Get-SystemSample;Start-Sleep -Milliseconds 750}
    $game=DetectGame;$pmResult=$null;$frame=$null
    if($game){
        $csv=Join-Path $dir 'presentmon.csv';$pmResult=Invoke-PresentMonCapture -ProcessId $game.Id -Seconds 20 -OutputCsv $csv
        if($pmResult.Success){$frame=Analyze-PresentMonCsv $csv}
    } else { $pmResult=[pscustomobject]@{Available=[bool](Get-PresentMonPath);Success=$false;Reason='No learned game is currently running';Csv=$null} }
    $after=Get-RecentStabilityEvents -Minutes 1
    $report=[ordered]@{Schema='D7.MEASUREMENT.v1';Generated=(Get-Date).ToString('o');Game=$(if($game){[ordered]@{Name=$game.ProcessName;Pid=$game.Id;Path=$game.Path}}else{$null});SystemSamples=@($samples);PresentMon=$pmResult;FrameAnalysis=$frame;StabilityBefore=@($before);StabilityDuring=@($after);InterruptAudit=@(Get-InterruptAudit);NetworkAudit=@(Get-NetworkDeepAudit);Xperf=Get-XperfStatus;Policy=[ordered]@{Mode='measure-first';WritesApplied=$false;RollbackRequired=$false}}
    $json=Join-Path $dir 'measurement.json';$report|ConvertTo-Json -Depth 10|Set-Content $json -Encoding UTF8
    $script:LastMeasurement=$report
    Log ('MEASUREMENT CORE: saved '+$json)
    if($frame -and $frame.Valid){Log ('FRAME RESULT: Avg '+$frame.AvgFPS+' FPS | 1% '+$frame.Low1FPS+' | 0.1% '+$frame.Low01FPS+' | P99 '+$frame.P99FrameMs+'ms | stutters '+$frame.Stutters)}else{Log ('FRAME RESULT: '+$pmResult.Reason)}
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
    if($files.Count -lt 2){[Windows.Forms.MessageBox]::Show('Need two measurement runs first.','D7 A/B Compare')|Out-Null;return}
    try{$a=Get-Content $files[1].FullName -Raw|ConvertFrom-Json;$b=Get-Content $files[0].FullName -Raw|ConvertFrom-Json}catch{[Windows.Forms.MessageBox]::Show('Could not read measurement files.','D7 A/B Compare')|Out-Null;return}
    $lines=New-Object System.Collections.Generic.List[string]
    $lines.Add('A/B MEASUREMENT COMPARISON')
    $lines.Add('A: '+$a.Generated)
    $lines.Add('B: '+$b.Generated)
    if($a.FrameAnalysis.Valid -and $b.FrameAnalysis.Valid){
        $lines.Add(('Avg FPS: {0} -> {1} ({2:+0.00;-0.00;0.00})' -f $a.FrameAnalysis.AvgFPS,$b.FrameAnalysis.AvgFPS,([double]$b.FrameAnalysis.AvgFPS-[double]$a.FrameAnalysis.AvgFPS)))
        $lines.Add(('1% Low: {0} -> {1} ({2:+0.00;-0.00;0.00})' -f $a.FrameAnalysis.Low1FPS,$b.FrameAnalysis.Low1FPS,([double]$b.FrameAnalysis.Low1FPS-[double]$a.FrameAnalysis.Low1FPS)))
        $lines.Add(('0.1% Low: {0} -> {1} ({2:+0.00;-0.00;0.00})' -f $a.FrameAnalysis.Low01FPS,$b.FrameAnalysis.Low01FPS,([double]$b.FrameAnalysis.Low01FPS-[double]$a.FrameAnalysis.Low01FPS)))
        $lines.Add(('P99 ms: {0} -> {1}' -f $a.FrameAnalysis.P99FrameMs,$b.FrameAnalysis.P99FrameMs))
        $lines.Add(('Stutters: {0} -> {1}' -f $a.FrameAnalysis.Stutters,$b.FrameAnalysis.Stutters))
    } else {$lines.Add('Frame A/B unavailable. Ensure a learned game is running and PresentMon is detected.')}
    [Windows.Forms.MessageBox]::Show(($lines -join "`n"),'D7 BLACKCORE A/B')|Out-Null
}
