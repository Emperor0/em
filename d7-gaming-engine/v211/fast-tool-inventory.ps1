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
