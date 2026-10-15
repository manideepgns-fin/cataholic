# Runs the cat on the CI runner's desktop beside a Notepad window and saves screenshots to $Out.
# Extra cat flags (e.g. a test scenario) go in -CatArgs.
param([string]$Exe, [string]$Out, [string[]]$CatArgs = @(), [int]$Seconds = 20, [int[]]$ShotsAt = @(3, 6, 10, 15))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class W { [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool r); }
'@
New-Item -ItemType Directory -Force $Out | Out-Null
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
"screen $($b.Width)x$($b.Height)" | Tee-Object -Append "$Out/log.txt"
# A window for the cat to land on: Notepad, in the middle of the screen
$np = Start-Process notepad -PassThru
Start-Sleep 2
$h = (Get-Process -Id $np.Id).MainWindowHandle
if ($h -ne 0) { [W]::MoveWindow($h, [int]($b.Width * 0.25), [int]($b.Height * 0.40), [int]($b.Width * 0.5), [int]($b.Height * 0.35), $true) | Out-Null }
$cat = Start-Process $Exe -ArgumentList (@('--quit-after', $Seconds) + $CatArgs) -PassThru `
  -RedirectStandardOutput "$Out/cat-stdout.txt" -RedirectStandardError "$Out/cat-stderr.txt"
$t0 = Get-Date
foreach ($s in $ShotsAt) {
  while (((Get-Date) - $t0).TotalSeconds -lt $s) { Start-Sleep -Milliseconds 100 }
  $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
  $bmp.Save("$Out/shot-$($s)s.png"); $g.Dispose(); $bmp.Dispose()
  $cpu = try { (Get-Process -Id $cat.Id).TotalProcessorTime.TotalSeconds } catch { -1 }
  $mem = try { [int]((Get-Process -Id $cat.Id).WorkingSet64 / 1MB) } catch { -1 }
  "t=$($s)s alive=$(-not $cat.HasExited) cpu=$($cpu)s mem=$($mem)MB" | Tee-Object -Append "$Out/log.txt"
}
$cat.WaitForExit(($Seconds + 10) * 1000) | Out-Null
"cat exit=$($cat.ExitCode)" | Tee-Object -Append "$Out/log.txt"
Stop-Process -Id $np.Id -ErrorAction SilentlyContinue
if ($cat.ExitCode -ne 0) { throw "the cat exited with $($cat.ExitCode)" }
