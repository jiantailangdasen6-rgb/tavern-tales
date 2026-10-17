# Online test over Unity Relay: a host and two friends on this PC, each with a bot (-autoplay) and the demo AI.
# Usage: powershell -ExecutionPolicy Bypass -File Tools/OnlineTest.ps1 [-Seconds 90] [-Shots "40,70"] [-Live]
# -Live uses the real AI on the host (Gemini quota). Logs and screenshots go to Logs/online/.
param([int]$Seconds = 90, [string]$Shots = "40,70", [switch]$Live)

$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "Builds\windows\Tavern.exe"
$out = Join-Path $root "Logs\online"
New-Item -ItemType Directory -Force $out | Out-Null
Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $out "*")
$code = Join-Path $out "code.txt"
$demo = @()
if (-not $Live) { $demo = @("-demo") }
$common = @("-autoplay", "8", "-screen-width", "1600", "-screen-height", "900", "-screen-fullscreen", "0", "-mute", "-autoquit", "$Seconds", "-shotat", $Shots)

$host1 = Start-Process -FilePath $exe -PassThru -ArgumentList ($demo + $common + @(
    "-onlinehost", "-presetchar", "0", "-playername", "Host", "-profile", "host", "-codefile", $code, "-startwhen", "3",
    "-savefile", (Join-Path $out "host-save.json"), "-screenshot", (Join-Path $out "host.png"), "-logFile", (Join-Path $out "host.log")))
Start-Sleep -Seconds 2
$c1 = Start-Process -FilePath $exe -PassThru -ArgumentList ($common + @(
    "-onlinejoinfile", $code, "-presetchar", "1", "-playername", "Friend1", "-profile", "friend1",
    "-screenshot", (Join-Path $out "friend1.png"), "-logFile", (Join-Path $out "friend1.log")))
Start-Sleep -Seconds 3
$c2 = Start-Process -FilePath $exe -PassThru -ArgumentList ($common + @(
    "-onlinejoinfile", $code, "-presetchar", "2", "-playername", "Friend2", "-profile", "friend2",
    "-screenshot", (Join-Path $out "friend2.png"), "-logFile", (Join-Path $out "friend2.log")))

$all = @($host1, $c1, $c2)
$deadline = (Get-Date).AddSeconds($Seconds + 40)
while ((Get-Date) -lt $deadline -and ($all | Where-Object { -not $_.HasExited }).Count -gt 0) { Start-Sleep -Seconds 1 }
$all | Where-Object { -not $_.HasExited } | ForEach-Object { Stop-Process -Id $_.Id -Force }

"code: " + $(if (Test-Path $code) { Get-Content $code } else { "(none)" })
foreach ($name in "host", "friend1", "friend2") {
    $log = Join-Path $out "$name.log"
    $issues = Select-String -Path $log -Pattern "Exception", "\[Online\]", "\[Table\]" -ErrorAction SilentlyContinue | Select-Object -First 5
    "$name : " + $(if ($issues) { ($issues | ForEach-Object { $_.Line }) -join " | " } else { "no errors" })
}
Get-ChildItem $out -Filter *.png | ForEach-Object { $_.Name }
