# The auto-update proof (lane win-update): a feed of our own served from this job, shaped like the GitHub releases API,
# and two real runs of Cataholic.exe 0.1.0 from temp folders:
#   1. a wrong .sha256  → the cat finds 0.1.1, refuses it, keeps running, the folder is untouched (negative case)
#   2. a right .sha256  → finds 0.1.1 (and ignores the Mac v9.9.9, a prerelease and a draft), swaps the file, the new
#      process starts and the old one exits, ONE Cataholic remains, the exe reports 0.1.1, Cataholic.exe.old is gone.
# -Exe: the normal 0.1.0 publish. -NewDir: a folder holding Cataholic-win-x64.zip + .sha256 of the 0.1.1 build. Logs go to $Out/log.txt.
param([string]$Exe, [string]$NewDir, [string]$Out, [int]$Port = 8765)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path; $Exe = (Resolve-Path $Exe).Path; $NewDir = (Resolve-Path $NewDir).Path
$log = "$Out/log.txt"
function Log($m) { $m | Tee-Object -Append $log }
$failures = @()
function Check($ok, $what) { if ($ok) { Log "PASS  $what" } else { Log "FAIL  $what"; $script:failures += $what } }
function Cats { @(Get-Process -Name Cataholic -ErrorAction SilentlyContinue) }
function Ver($path) { (Get-Item $path).VersionInfo.FileVersion }
function WaitFor($secs, [scriptblock]$cond) { $t = Get-Date; while (((Get-Date) - $t).TotalSeconds -lt $secs) { if (& $cond) { return $true }; Start-Sleep -Milliseconds 250 }; return $false }
function TraceText($f) { try { if (Test-Path $f) { [string](Get-Content $f -Raw) } else { '' } } catch { '' } }

Check ((Cats).Count -eq 0) 'no Cataholic running before the test'

# ── the feed ──
$tmp = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { $env:TEMP }     # the feed and the test folders stay out of the uploaded artifact
$feed = Join-Path $tmp 'feed'; New-Item -ItemType Directory -Force "$feed/good", "$feed/bad" | Out-Null
$base = "http://127.0.0.1:$Port"
function Asset($dir, $name) { @{ name = $name; browser_download_url = "$base/$dir/$name" } }
function Rel($tag, $dir, $draft = $false, $pre = $false) {
  @{ tag_name = $tag; name = $tag; draft = $draft; prerelease = $pre; assets = @((Asset $dir 'Cataholic-win-x64.zip'), (Asset $dir 'Cataholic-win-x64.zip.sha256')) }
}
foreach ($d in 'good', 'bad') {
  Copy-Item "$NewDir/Cataholic-win-x64.zip" "$feed/$d/"
  # the Mac release comes FIRST and highest: a feed that took "the newest" would take it
  $rels = @(
    (Rel 'v9.9.9' 'mac'), (Rel 'win-v0.3.0' 'nope' $true), (Rel 'win-v0.2.0' 'nope' $false $true),
    (Rel 'win-v0.1.1' $d), (Rel 'win-v0.1.0' $d))
  ConvertTo-Json -InputObject $rels -Depth 6 | Set-Content "$feed/$d/releases.json" -Encoding utf8
}
Copy-Item "$NewDir/Cataholic-win-x64.zip.sha256" "$feed/good/"
$realHash = (Get-Content "$NewDir/Cataholic-win-x64.zip.sha256" -Raw).Trim().Split(' ')[0]
$wrong = ('0' * 64); if ($realHash -eq $wrong) { $wrong = ('f' * 64) }
"$wrong  Cataholic-win-x64.zip" | Set-Content "$feed/bad/Cataholic-win-x64.zip.sha256" -Encoding ascii
Log "feed: real sha256 $realHash; the bad feed says $wrong"
$srv = Start-Process python -ArgumentList '-m', 'http.server', $Port, '--bind', '127.0.0.1', '--directory', $feed -PassThru -WindowStyle Hidden `
  -RedirectStandardOutput "$Out/server-out.txt" -RedirectStandardError "$Out/server-err.txt"
Check (WaitFor 20 { try { (Invoke-WebRequest "$base/good/releases.json" -UseBasicParsing).StatusCode -eq 200 } catch { $false } }) 'the local feed answers'
Log ("good/releases.json:`n" + (Get-Content "$feed/good/releases.json" -Raw))

function Shot($name) {
  $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
  $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
  $bmp.Save("$Out/$name.png"); $g.Dispose(); $bmp.Dispose()
}
function Start-Cat($folder, $feedUrl, $trace) {
  New-Item -ItemType Directory -Force $folder | Out-Null
  Copy-Item $Exe "$folder/Cataholic.exe" -Force
  Start-Process "$folder/Cataholic.exe" -ArgumentList '--scenario', 'update', '--update-feed', $feedUrl, '--trace', $trace -PassThru `
    -RedirectStandardOutput "$Out/stdout-$(Split-Path $folder -Leaf).txt" -RedirectStandardError "$Out/stderr-$(Split-Path $folder -Leaf).txt"
}

try {
  # ── 1. negative: a wrong .sha256 ──
  Log '== negative case: the .sha256 does not match the zip'
  $badDir = Join-Path $tmp 'bad'               # Join-Path: Process.Path comes back with backslashes only
  Remove-Item $badDir -Recurse -Force -ErrorAction SilentlyContinue
  $trB = "$Out/trace-bad.txt"
  $cat = Start-Cat $badDir "$base/bad/releases.json" $trB
  $pidB = $cat.Id
  Check (WaitFor 40 { (TraceText $trB) -match 'MISMATCH' }) 'bad hash: the cat found 0.1.1, downloaded it and reported a sha256 mismatch'
  Start-Sleep 8                                       # long enough for a (wrong) swap to have happened
  Check (-not $cat.HasExited -and (Cats).Count -eq 1 -and (Cats)[0].Id -eq $pidB) 'bad hash: the same 0.1.0 process still runs, and only it'
  Check ((Ver "$badDir/Cataholic.exe") -like '0.1.0.*') "bad hash: the exe in the folder still reports 0.1.0 ($(Ver "$badDir/Cataholic.exe"))"
  Check (-not (Test-Path "$badDir/Cataholic.exe.old") -and -not (Test-Path "$badDir/Cataholic.exe.new")) 'bad hash: no .old / .new left in the folder'
  Check (-not ((TraceText $trB) -match 'swapped')) 'bad hash: no swap in the log'
  Log "-- trace (bad):"; Log ((TraceText $trB) -split "`n" | Where-Object { $_ -match 'update ' }) 
  Stop-Process -Id $pidB -Force; $cat.WaitForExit(5000) | Out-Null
  Check (WaitFor 10 { (Cats).Count -eq 0 }) 'bad hash: stopped the test cat'

  # ── 2. the real update ──
  Log '== update case: the right .sha256'
  $goodDir = Join-Path $tmp 'good'
  Remove-Item $goodDir -Recurse -Force -ErrorAction SilentlyContinue
  $trG = "$Out/trace-good.txt"
  $cat = Start-Cat $goodDir "$base/good/releases.json" $trG
  $oldPid = $cat.Id
  Log "0.1.0 started: pid $oldPid, exe reports $(Ver "$goodDir/Cataholic.exe")"
  Check ((Ver "$goodDir/Cataholic.exe") -like '0.1.0.*') 'good: the exe we start is 0.1.0'
  Check (WaitFor 60 { (TraceText $trG) -match 'swapped' }) 'good: the cat swapped the file'
  Check (WaitFor 20 { -not (Get-Process -Id $oldPid -ErrorAction SilentlyContinue) }) 'good: the old process exited'
  $newOk = WaitFor 30 { $c = Cats; $c.Count -ge 1 -and ($c | Where-Object { $_.Id -ne $oldPid }) }
  Check $newOk 'good: a new Cataholic process started'
  Start-Sleep 6                                       # let the new cat settle; .old is deleted a moment after it starts
  $now = Cats
  Log ("Cataholic processes now: " + (($now | ForEach-Object { "pid $($_.Id) $($_.Path)" }) -join '; '))
  Check ($now.Count -eq 1) "good: exactly ONE Cataholic process remains ($($now.Count))"
  Check ($now.Count -eq 1 -and $now[0].Id -ne $oldPid -and $now[0].Path -eq "$goodDir\Cataholic.exe") 'good: it is a new pid running the same path'
  Check ((Ver "$goodDir/Cataholic.exe") -like '0.1.1.*') "good: the exe in the folder now reports 0.1.1 ($(Ver "$goodDir/Cataholic.exe"))"
  Check (WaitFor 15 { -not (Test-Path "$goodDir/Cataholic.exe.old") }) 'good: Cataholic.exe.old is gone after the new one started'
  Check (-not (Test-Path "$goodDir/Cataholic.exe.new")) 'good: no .new left'
  Check (-not (Test-Path "$env:LOCALAPPDATA/Cataholic/update")) 'good: the update download folder is cleaned up'
  Start-Sleep 5
  $t = TraceText $trG
  Check ($t -match 'update skip v9\.9\.9') 'good: the Mac v9.9.9 release was ignored'
  Check ($t -match 'update skip win-v0\.3\.0: draft' -and $t -match 'update skip win-v0\.2\.0: draft or prerelease') 'good: the draft and the prerelease were ignored'
  Check ($t -match 'update found win-v0\.1\.1: 0\.1\.1 > 0\.1\.0') 'good: it found win-v0.1.1 (higher than the running 0.1.0)'
  Check ($t -match 'update sha256 ok') 'good: the sha256 was checked and matched'
  Check ($t -match 'update new exe reports 0\.1\.1') 'good: the unzipped exe reports the tag version before the swap'
  Check ($t -match 'update after update: now running 0\.1\.1') 'good: the relaunched cat says it runs 0.1.1'
  Check ($t -match 'update removed Cataholic\.exe\.old') 'good: the new cat removed Cataholic.exe.old'
  Check ($t -match 'update up to date \(win-v0\.1\.1 is not newer than 0\.1\.1\)') 'good: the 0.1.1 cat checks the same feed and stays put (no update loop)'
  Check ((Cats).Count -eq 1) 'good: still exactly one Cataholic process after the second check'
  Shot 'after-update'
  Log "-- trace (good), update lines:"; Log ((TraceText $trG) -split "`n" | Where-Object { $_ -match 'update ' })
} finally {
  Get-Process -Name Cataholic -ErrorAction SilentlyContinue | Stop-Process -Force
  if ($srv) { Stop-Process -Id $srv.Id -Force -ErrorAction SilentlyContinue }
}
if ($failures) { throw "update proof failed: $($failures -join ' | ')" }
Log 'update proof: all checks passed'
