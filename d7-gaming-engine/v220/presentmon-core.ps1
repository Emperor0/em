# D7 BLACKCORE v2.2.0 - PresentMon verified acquisition + measurement runtime
# Official source: GameTechDev/PresentMon. D7 verifies the pinned SHA256 from the online tool catalog.

$ToolCatalogUrl='https://raw.githubusercontent.com/Emperor0/em/main/d7-gaming-engine/intelligence/tool-catalog.json'

function Get-D7ToolCatalog {
    try {
        [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
        return Invoke-RestMethod -Uri $ToolCatalogUrl -TimeoutSec 15 -ErrorAction Stop
    } catch {
        Log ('Tool catalog unavailable: '+$_.Exception.Message)
        return $null
    }
}

function Get-PresentMonPath {
    $candidates=@(
        (Join-Path $ToolRoot 'PresentMon\PresentMon.exe'),
        (Join-Path $env:ProgramFiles 'Intel\PresentMon\PresentMon.exe'),
        (Join-Path $env:ProgramFiles 'PresentMon\PresentMon.exe'),
        (Join-Path $env:ProgramFiles 'PresentMon\PresentMon-2.5.1-x64.exe')
    )
    foreach($p in $candidates){ if($p -and (Test-Path -LiteralPath $p)){ return $p } }
    try {
        $cmd=Get-Command 'PresentMon.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
        if($cmd -and $cmd.Source -and (Test-Path -LiteralPath $cmd.Source)){ return $cmd.Source }
    } catch {}
    return $null
}

function Ensure-PresentMon {
    $existing=Get-PresentMonPath
    if($existing){
        return [pscustomobject]@{Success=$true;Path=$existing;InstalledNow=$false;Version=(FileVersion $existing);Reason=$null}
    }

    $catalog=Get-D7ToolCatalog
    if(-not $catalog){ return [pscustomobject]@{Success=$false;Path=$null;InstalledNow=$false;Version=$null;Reason='Online D7 tool catalog unavailable'} }
    $def=@($catalog.tools | Where-Object { $_.id -eq 'presentmon' }) | Select-Object -First 1
    if(-not $def){ return [pscustomobject]@{Success=$false;Path=$null;InstalledNow=$false;Version=$null;Reason='PresentMon is missing from the D7 tool catalog'} }

    $dir=Join-Path $ToolRoot 'PresentMon'
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $dest=Join-Path $dir 'PresentMon.exe'
    $tmp=Join-Path $dir 'PresentMon.download'
    try {
        if(Test-Path $tmp){Remove-Item $tmp -Force -ErrorAction SilentlyContinue}
        Log ('FRAME LAB: downloading official PresentMon '+$def.version+'...')
        [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri ([string]$def.downloadUrl) -OutFile $tmp -UseBasicParsing -TimeoutSec 60 -ErrorAction Stop
        $hash=(Get-FileHash -LiteralPath $tmp -Algorithm SHA256).Hash.ToLowerInvariant()
        $expected=([string]$def.sha256).ToLowerInvariant()
        if($hash -ne $expected){
            Remove-Item $tmp -Force -ErrorAction SilentlyContinue
            throw ('PresentMon SHA256 mismatch. Expected '+$expected+' got '+$hash)
        }
        Move-Item -LiteralPath $tmp -Destination $dest -Force
        [ordered]@{
            id='presentmon';version=[string]$def.version;source=[string]$def.downloadUrl;sha256=$hash;
            installedUtc=(Get-Date).ToUniversalTime().ToString('o');license=[string]$def.license;trust=[string]$def.trust
        } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $dir 'd7-tool.json') -Encoding UTF8
        Log ('FRAME LAB: PresentMon verified and ready: '+$dest)
        return [pscustomobject]@{Success=$true;Path=$dest;InstalledNow=$true;Version=[string]$def.version;Reason=$null}
    } catch {
        try{if(Test-Path $tmp){Remove-Item $tmp -Force -ErrorAction SilentlyContinue}}catch{}
        Log ('FRAME LAB: PresentMon setup failed: '+$_.Exception.Message)
        return [pscustomobject]@{Success=$false;Path=$null;InstalledNow=$false;Version=$null;Reason=$_.Exception.Message}
    }
}

function Invoke-PresentMonCapture {
    param([int]$ProcessId,[int]$Seconds=30,[string]$OutputCsv)
    $pm=Get-PresentMonPath
    if(-not $pm){ return [pscustomobject]@{Available=$false;Success=$false;Reason='PresentMon not detected';Csv=$null;Samples=@();TimedOut=$false} }
    $samples=New-Object System.Collections.Generic.List[object]
    $session=('D7BLACKCORE_'+$ProcessId+'_'+(Get-Date -Format 'HHmmss'))
    $args=@(
        '--process_id',([string]$ProcessId),
        '--delay','3',
        '--timed',([string]$Seconds),
        '--terminate_after_timed',
        '--output_file',('"'+$OutputCsv+'"'),
        '--v2_metrics',
        '--exclude_dropped',
        '--no_console_stats',
        '--session_name',$session
    )
    $p=$null
    try {
        $p=Start-Process -FilePath $pm -ArgumentList $args -PassThru -WindowStyle Hidden -ErrorAction Stop
        $deadline=(Get-Date).AddSeconds($Seconds+15)
        $nextSample=Get-Date
        while(-not $p.HasExited){
            [Windows.Forms.Application]::DoEvents()
            if((Get-Date) -ge $nextSample){
                try{[void]$samples.Add((Get-SystemSample))}catch{}
                $nextSample=(Get-Date).AddSeconds(2.5)
            }
            if((Get-Date) -gt $deadline){
                try{Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}catch{}
                return [pscustomobject]@{Available=$true;Success=$false;Reason='PresentMon capture timed out';Csv=$null;Samples=@($samples);TimedOut=$true}
            }
            Start-Sleep -Milliseconds 100
            try{$p.Refresh()}catch{}
        }
        if((Test-Path -LiteralPath $OutputCsv) -and ((Get-Item -LiteralPath $OutputCsv).Length -gt 64)){
            return [pscustomobject]@{Available=$true;Success=$true;Reason=$null;Csv=$OutputCsv;Samples=@($samples);TimedOut=$false;ExitCode=$p.ExitCode}
        }
        return [pscustomobject]@{Available=$true;Success=$false;Reason=('No usable CSV produced. ExitCode '+$p.ExitCode);Csv=$null;Samples=@($samples);TimedOut=$false;ExitCode=$p.ExitCode}
    } catch {
        if($p -and -not $p.HasExited){try{Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}catch{}}
        return [pscustomobject]@{Available=$true;Success=$false;Reason=$_.Exception.Message;Csv=$null;Samples=@($samples);TimedOut=$false}
    }
}

function Run-MeasurementBaseline {
    $game=DetectGame
    if(-not $game){ throw 'No learned game is currently running. Start the learned game first.' }

    $pmSetup=Ensure-PresentMon
    if(-not $pmSetup.Success){ throw ('PresentMon setup failed: '+$pmSetup.Reason) }

    Log ('MEASUREMENT CORE v2: preparing '+$game.ProcessName+' (PID '+$game.Id+').')
    [Windows.Forms.MessageBox]::Show('FRAME LAB is ready.`n`nPress OK, return to the game immediately, and play the same scene normally.`nCapture begins after 3 seconds and runs for 30 seconds.','D7 BLACKCORE - FRAME LAB') | Out-Null

    $ts=Get-Date -Format 'yyyyMMdd_HHmmss'
    $dir=Join-Path $MeasureRoot $ts
    New-Item $dir -ItemType Directory -Force|Out-Null
    $csv=Join-Path $dir 'presentmon.csv'
    $before=Get-RecentStabilityEvents -Minutes 30
    $pmResult=Invoke-PresentMonCapture -ProcessId $game.Id -Seconds 30 -OutputCsv $csv
    $frame=$null
    if($pmResult.Success){$frame=Analyze-PresentMonCsv $csv}
    $after=Get-RecentStabilityEvents -Minutes 2

    $pmMeta=$null
    try{
        $metaPath=Join-Path $ToolRoot 'PresentMon\d7-tool.json'
        if(Test-Path $metaPath){$pmMeta=Get-Content $metaPath -Raw|ConvertFrom-Json}
    }catch{}

    $report=[ordered]@{
        Schema='D7.MEASUREMENT.v2'
        Generated=(Get-Date).ToString('o')
        Game=[ordered]@{Name=$game.ProcessName;Pid=$game.Id;Path=$game.Path}
        Capture=[ordered]@{DelaySeconds=3;DurationSeconds=30;Mode='PresentMon-v2';SameSceneRequired=$true}
        SystemSamples=@($pmResult.Samples)
        PresentMon=$pmResult
        PresentMonTool=$pmMeta
        FrameAnalysis=$frame
        StabilityBefore=@($before)
        StabilityDuring=@($after)
        InterruptAudit=@(Get-InterruptAudit)
        NetworkAudit=@(Get-NetworkDeepAudit)
        Xperf=Get-XperfStatus
        HAGS=(RegGet 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' 'HwSchMode')
        Policy=[ordered]@{Mode='measure-first';WritesApplied=$false;RollbackRequired=$false;PrimaryJudge='frametime-and-lows'}
    }
    $json=Join-Path $dir 'measurement.json'
    $report|ConvertTo-Json -Depth 10|Set-Content $json -Encoding UTF8
    $script:LastMeasurement=$report
    Log ('MEASUREMENT CORE v2: saved '+$json)
    if($frame -and $frame.Valid){
        $summary=('Avg '+$frame.AvgFPS+' FPS | 1% '+$frame.Low1FPS+' | 0.1% '+$frame.Low01FPS+' | P99 '+$frame.P99FrameMs+' ms | stutters '+$frame.Stutters)
        Log ('FRAME RESULT: '+$summary)
        [Windows.Forms.MessageBox]::Show(($summary+'`n`nRun the same scene again after a tweak, then use A/B COMPARE.'),'D7 FRAME RESULT')|Out-Null
    } else {
        $reason=if($pmResult.Reason){$pmResult.Reason}else{'PresentMon CSV could not be analyzed'}
        Log ('FRAME RESULT unavailable: '+$reason)
        [Windows.Forms.MessageBox]::Show(('Frame capture failed: '+$reason+'`n`nThe diagnostic JSON was still saved.'),'D7 FRAME LAB')|Out-Null
    }
    Start-Process explorer.exe $dir
    return $report
}

function Compare-LastMeasurements {
    $parsed=@()
    foreach($f in @(Get-ChildItem $MeasureRoot -Filter measurement.json -Recurse -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 20)){
        try{
            $x=Get-Content $f.FullName -Raw|ConvertFrom-Json
            if($x.Game -and $x.Game.Name -and $x.FrameAnalysis -and $x.FrameAnalysis.Valid){$parsed += [pscustomobject]@{File=$f;Data=$x;Game=[string]$x.Game.Name}}
        }catch{}
    }
    if($parsed.Count -lt 2){[Windows.Forms.MessageBox]::Show('Need two valid PresentMon measurement runs first.','D7 A/B Compare')|Out-Null;return}
    $gameName=$parsed[0].Game
    $same=@($parsed | Where-Object {$_.Game -eq $gameName} | Select-Object -First 2)
    if($same.Count -lt 2){[Windows.Forms.MessageBox]::Show(('Need two valid runs for the same game: '+$gameName),'D7 A/B Compare')|Out-Null;return}
    $b=$same[0].Data;$a=$same[1].Data
    $avgA=[double]$a.FrameAnalysis.AvgFPS;$avgB=[double]$b.FrameAnalysis.AvgFPS
    $lowA=[double]$a.FrameAnalysis.Low1FPS;$lowB=[double]$b.FrameAnalysis.Low1FPS
    $p99A=[double]$a.FrameAnalysis.P99FrameMs;$p99B=[double]$b.FrameAnalysis.P99FrameMs
    $stA=[int]$a.FrameAnalysis.Stutters;$stB=[int]$b.FrameAnalysis.Stutters
    $avgPct=if($avgA-ne0){(($avgB-$avgA)/$avgA)*100}else{0}
    $lowPct=if($lowA-ne0){(($lowB-$lowA)/$lowA)*100}else{0}
    $p99Pct=if($p99A-ne0){(($p99B-$p99A)/$p99A)*100}else{0}
    $verdict='INCONCLUSIVE'
    if($lowPct -ge 2 -and $p99Pct -le -2 -and $avgPct -ge -1 -and $stB -le $stA){$verdict='KEEP'}
    elseif($lowPct -le -3 -or $p99Pct -ge 5 -or $stB -gt ($stA+3)){$verdict='ROLLBACK'}
    $lines=@(
        'A/B MEASUREMENT COMPARISON - '+$gameName,
        ('Avg FPS: {0} -> {1} ({2:+0.00;-0.00;0.00}%)' -f $avgA,$avgB,$avgPct),
        ('1% Low: {0} -> {1} ({2:+0.00;-0.00;0.00}%)' -f $lowA,$lowB,$lowPct),
        ('0.1% Low: {0} -> {1}' -f $a.FrameAnalysis.Low01FPS,$b.FrameAnalysis.Low01FPS),
        ('P99: {0} -> {1} ms ({2:+0.00;-0.00;0.00}%)' -f $p99A,$p99B,$p99Pct),
        ('Stutters: {0} -> {1}' -f $stA,$stB),
        '',('D7 VERDICT: '+$verdict)
    )
    $cmp=[ordered]@{Schema='D7.COMPARE.v1';Generated=(Get-Date).ToString('o');Game=$gameName;A=$a.Generated;B=$b.Generated;AvgFpsPct=[math]::Round($avgPct,2);Low1Pct=[math]::Round($lowPct,2);P99Pct=[math]::Round($p99Pct,2);StuttersA=$stA;StuttersB=$stB;Verdict=$verdict}
    $cmp|ConvertTo-Json -Depth 4|Set-Content (Join-Path $MeasureRoot ('compare_'+(Get-Date -Format 'yyyyMMdd_HHmmss')+'.json')) -Encoding UTF8
    [Windows.Forms.MessageBox]::Show(($lines -join "`n"),'D7 BLACKCORE A/B')|Out-Null
}
