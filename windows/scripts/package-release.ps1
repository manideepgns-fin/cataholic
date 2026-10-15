# Builds the Windows release files into $Out: Cataholic-win-x64.zip (ONLY Cataholic.exe) and Cataholic-win-x64.zip.sha256.
# Used by .github/workflows/windows-release.yml (the real release) and by windows.yml (the dry run and the update test's 0.1.1).
#   -Tag win-v0.1.0   the release tag: it must equal win-v<csproj Version>, or this stops (the updater trusts the tag)
#   -Version 0.1.1    publish with a different version (the update test builds a "newer" cat this way); no tag check
param([string]$Out, [string]$Tag = '', [string]$Version = '')
$ErrorActionPreference = 'Stop'
$proj = 'windows/Cataholic.Win/Cataholic.Win.csproj'
$csproj = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($Tag) {
  if ($Tag -notmatch '^win-v\d+\.\d+\.\d+$') { throw "tag '$Tag' is not win-v<major>.<minor>.<patch> (Mac releases are v*, and are not ours to make)" }
  if ($Tag -ne "win-v$csproj") { throw "tag $Tag does not match the csproj <Version> $csproj" }
}
$ver = if ($Version) { $Version } else { $csproj }
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path
$pub = Join-Path $Out 'publish'
$extra = @(); if ($Version) { $extra = @("-p:Version=$Version") }
dotnet publish windows/Cataholic.Win -c Release -o $pub @extra
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
$exe = Join-Path $pub 'Cataholic.exe'
$fv = (Get-Item $exe).VersionInfo
"exe reports $($fv.FileVersion) (product $($fv.ProductVersion)), wanted $ver"
if ($fv.FileVersion -notlike "$ver.*" -and $fv.FileVersion -ne $ver) { throw "the published exe reports $($fv.FileVersion), not $ver" }

# smoke test: the published exe draws the coats and exits 0
$png = Join-Path $Out 'smoke-coats.png'
$p = Start-Process $exe -ArgumentList '--render-coats', $png -PassThru -Wait
if ($p.ExitCode -ne 0 -or -not (Test-Path $png)) { throw "smoke test (--render-coats) failed ($($p.ExitCode))" }
"smoke test: --render-coats ok ($((Get-Item $png).Length) bytes)"

# the zip holds only Cataholic.exe, at its root
$zip = Join-Path $Out 'Cataholic-win-x64.zip'
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path $exe -DestinationPath $zip
Add-Type -AssemblyName System.IO.Compression.FileSystem
$za = [System.IO.Compression.ZipFile]::OpenRead($zip)
$names = $za.Entries | ForEach-Object { $_.FullName }
$za.Dispose()
if (($names -join ',') -ne 'Cataholic.exe') { throw "the zip must hold only Cataholic.exe, it holds: $names" }
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
"$hash  Cataholic-win-x64.zip" | Set-Content "$zip.sha256" -Encoding ascii
"zip: $((Get-Item $zip).Length) bytes, sha256 $hash"
