function LearnForeground {
    [Windows.Forms.MessageBox]::Show('Press OK, switch to the game within 4 seconds.','D7 BLACKCORE') | Out-Null
    $form.Hide()
    try {
        Start-Sleep -Seconds 4
        if (-not ('D7Foreground.NativeMethods' -as [type])) {
            $code = @'
using System;
using System.Runtime.InteropServices;
namespace D7Foreground {
    public static class NativeMethods {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}
'@
            Add-Type -TypeDefinition $code -ErrorAction Stop
        }

        $h = [D7Foreground.NativeMethods]::GetForegroundWindow()
        [uint32]$processId = 0
        [void][D7Foreground.NativeMethods]::GetWindowThreadProcessId($h,[ref]$processId)
        if ($processId -le 0) { throw 'Foreground process could not be resolved.' }

        $p = Get-Process -Id $processId -ErrorAction Stop
        $path = $p.Path
        if (-not $path) { throw 'Foreground process path is unavailable. Try running D7 as administrator.' }

        $existing = @($script:LearnedGames | Where-Object { $_.Path -eq $path }).Count -gt 0
        if (-not $existing) {
            $script:LearnedGames += [pscustomobject]@{
                Name  = $p.ProcessName
                Path  = $path
                Added = (Get-Date).ToString('o')
            }
            $script:LearnedGames | ConvertTo-Json -Depth 5 | Set-Content $GamesFile -Encoding UTF8
            Log ('Learned game: '+$p.ProcessName+' | '+$path)
        } else {
            Log ('Game already learned: '+$p.ProcessName)
        }
        [Windows.Forms.MessageBox]::Show(('Game learned: '+$p.ProcessName),'D7 BLACKCORE') | Out-Null
    }
    catch {
        $msg = $_.Exception.Message
        Log ('Learn game failed: '+$msg)
        [Windows.Forms.MessageBox]::Show(('Learn Game failed: '+$msg+'`n`nKeep the game focused after pressing OK and try again.'),'D7 BLACKCORE') | Out-Null
    }
    finally {
        $form.Show()
        $form.Activate()
    }
}
