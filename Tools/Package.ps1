# Packs the Windows build for friends: dist/tavern-tales-<date>.zip with the game and Docs/dist/readme.txt.
# Usage: powershell -ExecutionPolicy Bypass -File Tools/Package.ps1   (build first with RunUnity.ps1 ... BuildWindows)
# Nothing is uploaded; send the zip by hand. Keep this file ASCII-only (Windows PowerShell 5.1).
$root = Split-Path -Parent $PSScriptRoot
$build = Join-Path $root "Builds\windows"
if (-not (Test-Path (Join-Path $build "Tavern.exe"))) { throw "no build at $build" }

$stamp = Get-Date -Format "yyyyMMdd"
$dist = Join-Path $root "dist"
$stage = Join-Path $dist "tavern-tales"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null

# Everything but Unity's debug-symbol folder ("..._BackUpThisFolder_ButDontShipItWithYourGame").
Get-ChildItem $build | Where-Object { $_.Name -notlike "*DontShip*" } | ForEach-Object {
    Copy-Item -Recurse -Force $_.FullName (Join-Path $stage $_.Name)
}
# The readme's file name is Japanese ("hajimeni"); built from code points so this script stays ASCII.
$readme = [string]::new([char[]]@(0x306F, 0x3058, 0x3081, 0x306B)) + ".txt"
Copy-Item (Join-Path $root "Docs\dist\readme.txt") (Join-Path $stage $readme)
# The same credits as the title screen's credits panel (Tabletop Audio's license requires them).
$utf8 = New-Object System.Text.UTF8Encoding $false
$credits = [IO.File]::ReadAllText((Join-Path $root "Unity\Assets\Resources\Music\credits.txt"), $utf8)
[IO.File]::AppendAllText((Join-Path $stage $readme), "`r`n" + $credits, $utf8)

$zip = Join-Path $dist "tavern-tales-$stamp.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal
$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
"packed: $zip ($mb MB)"
