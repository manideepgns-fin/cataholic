# The cataholic:// proof (lane win-links): runs `--scenario sign` beside Notepad, then drives it the way a user would —
# links opened through the shell (Start-Process "cataholic://…" → the registry key the cat wrote → a second Cataholic.exe
# --url "…" → the named pipe → the running cat), a bare second launch (a double-click), and a real Esc key press.
# Screenshots and a log go to $Out. ci-run.ps1 stays as it is: this one needs actions between the shots.
param([string]$Exe, [string]$Out, [int]$Seconds = 30)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool r);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}
'@
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path
$Exe = (Resolve-Path $Exe).Path
$log = "$Out/log.txt"
function Log($m) { $m | Tee-Object -Append $log }
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
Log "screen $($b.Width)x$($b.Height)"
(New-Object -ComObject Shell.Application).MinimizeAll(); Start-Sleep 1
$np = Start-Process notepad -PassThru
Start-Sleep 2
$h = (Get-Process -Id $np.Id).MainWindowHandle
if ($h -ne 0) { [W]::MoveWindow($h, [int]($b.Width * 0.25), [int]($b.Height * 0.40), [int]($b.Width * 0.5), [int]($b.Height * 0.35), $true) | Out-Null }
$cat = Start-Process $Exe -ArgumentList '--quit-after', $Seconds, '--scenario', 'sign', '--trace', "$Out\trace.txt" -PassThru `
  -RedirectStandardOutput "$Out/cat-stdout.txt" -RedirectStandardError "$Out/cat-stderr.txt"
$t0 = Get-Date
function At($s) { while (((Get-Date) - $t0).TotalSeconds -lt $s) { Start-Sleep -Milliseconds 50 } }
function Shot($s) {
  At $s
  $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
  $bmp.Save("$Out/shot-$($s)s.png"); $g.Dispose(); $bmp.Dispose()
  $cpu = try { (Get-Process -Id $cat.Id).TotalProcessorTime.TotalSeconds } catch { -1 }
  $mem = try { [int]((Get-Process -Id $cat.Id).WorkingSet64 / 1MB) } catch { -1 }
  Log "t=$($s)s alive=$(-not $cat.HasExited) cpu=$($cpu)s mem=$($mem)MB"
}
# a link, opened through the shell exactly as a user (or a browser) would
function Link($s, $url) { At $s; Log "t=$($s)s link $url"; Start-Process $url }
function Cats { (Get-Process -Name Cataholic -ErrorAction SilentlyContinue | Measure-Object).Count }

Shot 3                                            # the cat at rest, before any link (CPU baseline)
Log "registry: $((Get-ItemProperty 'HKCU:\Software\Classes\cataholic\shell\open\command' -ErrorAction SilentlyContinue).'(default)')"
Log "processes before: $(Cats)"
Link 4 'cataholic://sign?count=3&note=CI'         # sign up with 3, whisper "New notification from CI", two hops
Shot 5
Shot 8                                            # the whisper is gone (3 s), the sign stays
# a second launch with no link — what a double-click on the exe does: it must exit within ~1 s and leave one process
At 8.5
$sw = [Diagnostics.Stopwatch]::StartNew()
$p2 = Start-Process $Exe -PassThru
$null = $p2.Handle                                # keep the handle, or ExitCode reads empty
$done = $p2.WaitForExit(5000)
$sw.Stop()
Log "second launch: exited=$done after $($sw.ElapsedMilliseconds) ms (exit code $(if ($done) { $p2.ExitCode } else { '-' })); Cataholic processes now: $(Cats)"
if (-not $done) { Stop-Process -Id $p2.Id -ErrorAction SilentlyContinue }
Link 10.5 'cataholic://sign?count=12&note=CI'     # up again: whispers again, the sign reads 9+
Shot 11.5
Link 14 'cataholic://sign?count=0'                # down: the sign goes away, no whisper
Shot 15
Link 16 'cataholic://sign?count=2&note=Esc%20test'  # up: whisper…
At 16.9
[W]::keybd_event(0x1B, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 120; [W]::keybd_event(0x1B, 0, 2, [UIntPtr]::Zero)   # …Esc hushes it
Log "t=16.9s Esc pressed"
Shot 17.6                                         # sign 2, no bubble
Link 18.5 'cataholic://zoomies'
Shot 19.5
Shot 22
Log "processes at the end: $(Cats)"
$cat.WaitForExit(($Seconds + 10) * 1000) | Out-Null
Log "cat exit=$($cat.ExitCode)"
Stop-Process -Id $np.Id -ErrorAction SilentlyContinue
if ($cat.ExitCode -ne 0) { throw "the cat exited with $($cat.ExitCode)" }
