# Online test over Unity Relay on the new flow: a host and two (or three) friends on this PC, each with a bot (-autoplay) and the demo AI.
# Usage: powershell -ExecutionPolicy Bypass -File Tools/OnlineTest.ps1 [-Seconds 90] [-Shots "40,70"] [-Live] [-Friends 3] [-Rejoin 50] [-Drop 60]
# -Live uses the real AI on the host (Gemini quota). -Friends 3 makes a party of four.
# -Rejoin N: friend 1 quits after N seconds and comes back with the same profile (friend1b). -DropRejoin N: friend 1's connection drops after N seconds and
# it joins again from the same running game 5 seconds later (a network hiccup). -DropRoom N: the host's room is lost after N seconds and
# opens again under a new code (written to the code file). -Drop N: friend 2 quits after N seconds for good
# and the host lets their adventurer leave for tonight (-withdrawaway). -TestKey uses GEMINI_API_KEY_TEST with -Live; -Walk gives every bot the same
# walkthrough (each declares its own adventurer's part); -Difficulty gentle|standard|harsh; -Turns the bots' declarations. Logs and screenshots go to Logs/online/.
param([int]$Seconds = 90, [string]$Shots = "40,70", [switch]$Live, [switch]$TestKey, [int]$Friends = 2, [int]$Rejoin = 0, [int]$Drop = 0,
    [string]$Walk = "", [string]$Difficulty = "", [int]$Turns = 30, [int]$DropRejoin = 0, [int]$DropRoom = 0, [string]$Exe = "")

$root = Split-Path -Parent $PSScriptRoot
$exe = if ($Exe) { $Exe } else { Join-Path $root "Builds\windows\Tavern.exe" }
$out = Join-Path $root "Logs\online"
New-Item -ItemType Directory -Force $out | Out-Null
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue (Join-Path $out "*")
# each PC keeps its own files (roster, chronicle, tavern) in its own folder, as on separate PCs
$code = Join-Path $out "code.txt"
$demo = @()
if (-not $Live) { $demo = @("-demo") } elseif ($TestKey) { $demo = @("-testkey") }
function Common([int]$quit) {
    $c = @("-night", "-autoplay", "$Turns", "-screen-width", "1600", "-screen-height", "900", "-screen-fullscreen", "0", "-mute", "-autoquit", "$quit", "-shotat", $Shots)
    if ($Walk) { $c += @("-walk", (Resolve-Path $Walk).Path) }
    $c
}

$start = Get-Date
$all = @()
$hostArgs = $demo + (Common $Seconds) + @(
    "-onlinehost", "-presetchar", "0", "-playername", "Host", "-profile", "host", "-codefile", $code, "-startwhen", "$($Friends + 1)",
    "-savefile", (Join-Path $out "host/save.json"), "-screenshot", (Join-Path $out "host.png"), "-logFile", (Join-Path $out "host.log"))
if ($Drop -gt 0) { $hostArgs += "-withdrawaway" }
if ($DropRoom -gt 0) { $hostArgs += @("-droproom", "$DropRoom") }
if ($Difficulty) { $hostArgs += @("-difficulty", $Difficulty) }
$all += Start-Process -FilePath $exe -PassThru -ArgumentList $hostArgs
$names = @("host")
for ($i = 1; $i -le $Friends; $i++) {
    Start-Sleep -Seconds $(if ($i -eq 1) { 2 } else { 3 })
    $quit = $Seconds
    if ($i -eq 1 -and $Rejoin -gt 0) { $quit = $Rejoin }
    if ($i -eq 2 -and $Drop -gt 0) { $quit = $Drop }
    $extra = @()
    if ($i -eq 1 -and $DropRejoin -gt 0) { $extra = @("-droprejoin", "$DropRejoin") }
    $all += Start-Process -FilePath $exe -PassThru -ArgumentList ((Common $quit) + @(
        "-onlinejoinfile", $code, "-presetchar", "$i", "-playername", "Friend$i", "-profile", "friend$i",
        "-savefile", (Join-Path $out "friend$i/save.json"), "-screenshot", (Join-Path $out "friend$i.png"), "-logFile", (Join-Path $out "friend$i.log")) + $extra)
    $names += "friend$i"
}
if ($Rejoin -gt 0) {
    while (((Get-Date) - $start).TotalSeconds -lt $Rejoin + 10) { Start-Sleep -Seconds 1 }
    $left = [Math]::Max(30, $Seconds - $Rejoin - 10)
    $all += Start-Process -FilePath $exe -PassThru -ArgumentList ((Common $left) + @(
        "-onlinejoinfile", $code, "-presetchar", "1", "-playername", "Friend1", "-profile", "friend1",
        "-savefile", (Join-Path $out "friend1/save.json"), "-screenshot", (Join-Path $out "friend1b.png"), "-logFile", (Join-Path $out "friend1b.log")))
    $names += "friend1b"
}

$deadline = $start.AddSeconds($Seconds + 40)
while ((Get-Date) -lt $deadline -and ($all | Where-Object { -not $_.HasExited }).Count -gt 0) { Start-Sleep -Seconds 1 }
$all | Where-Object { -not $_.HasExited } | ForEach-Object { Stop-Process -Id $_.Id -Force }

"code: " + $(if (Test-Path $code) { Get-Content $code } else { "(none)" })
foreach ($name in $names) {
    $log = Join-Path $out "$name.log"
    $issues = Select-String -Path $log -Pattern "Exception", "\[Online\]", "\[Table\]", "\[Night\]" -ErrorAction SilentlyContinue | Select-Object -First 6
    "$name : " + $(if ($issues) { ($issues | ForEach-Object { $_.Line }) -join " | " } else { "no errors" })
}
Get-ChildItem $out -Filter *.png | ForEach-Object { $_.Name }
