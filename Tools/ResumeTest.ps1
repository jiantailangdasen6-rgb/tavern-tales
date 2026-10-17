# Online resume test (demo AI, bots):
#  1. host + 2 friends play for a while; friend1 drops at 45 s and comes back at 55 s under the same name
#  2. everyone quits; the host reopens the saved evening as a new room ("continue"), both friends rejoin by name
# Usage: powershell -ExecutionPolicy Bypass -File Tools/ResumeTest.ps1
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "Builds\windows\Tavern.exe"
$out = Join-Path $root "Logs\resume"
New-Item -ItemType Directory -Force $out | Out-Null
Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $out "*")
$save = Join-Path $out "host-save.json"
$view = @("-screen-width", "1280", "-screen-height", "720", "-screen-fullscreen", "0", "-mute")

function Friend($n, $codeFile, $quit, $tag, $shots) {
    Start-Process -FilePath $exe -PassThru -ArgumentList ($view + @("-autoplay", "20", "-autoquit", "$quit",
        "-onlinejoinfile", $codeFile, "-presetchar", "$n", "-playername", "Friend$n", "-profile", "friend$n",
        "-screenshot", (Join-Path $out "$tag.png"), "-shotat", $shots, "-logFile", (Join-Path $out "$tag.log")))
}

# Phase 1
$code1 = Join-Path $out "code1.txt"
$h = Start-Process -FilePath $exe -PassThru -ArgumentList ($view + @("-demo", "-autoplay", "20", "-autoquit", "80",
    "-onlinehost", "-presetchar", "0", "-playername", "Host", "-profile", "host", "-codefile", $code1, "-startwhen", "3",
    "-savefile", $save, "-screenshot", (Join-Path $out "p1-host.png"), "-shotat", "70", "-logFile", (Join-Path $out "p1-host.log")))
Start-Sleep -Seconds 2
$f1 = Friend 1 $code1 45 "p1-friend1" "40"
Start-Sleep -Seconds 3
$f2 = Friend 2 $code1 80 "p1-friend2" "70"
Start-Sleep -Seconds 55
$f1b = Friend 1 $code1 80 "p1-friend1-back" "20"
foreach ($p in @($h, $f1, $f2, $f1b)) { $p.WaitForExit(60000) | Out-Null; if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } }

# Phase 2
$code2 = Join-Path $out "code2.txt"
$h2 = Start-Process -FilePath $exe -PassThru -ArgumentList ($view + @("-demo", "-autoplay", "6", "-autoquit", "60", "-continue",
    "-playername", "Host", "-profile", "host", "-codefile", $code2, "-startwhen", "3",
    "-savefile", $save, "-screenshot", (Join-Path $out "p2-host.png"), "-shotat", "15,50", "-logFile", (Join-Path $out "p2-host.log")))
Start-Sleep -Seconds 2
$g1 = Friend 1 $code2 60 "p2-friend1" "50"
Start-Sleep -Seconds 3
$g2 = Friend 2 $code2 60 "p2-friend2" "50"
foreach ($p in @($h2, $g1, $g2)) { $p.WaitForExit(90000) | Out-Null; if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } }

foreach ($f in Get-ChildItem $out -Filter *.log) {
    $issues = Select-String -Path $f.FullName -Pattern "Exception", "\[Online\]" | Select-Object -First 3
    $lines = (Select-String -Path $f.FullName -Pattern "\[Log\]").Count
    "$($f.Name): $lines lines shown; " + $(if ($issues) { ($issues | ForEach-Object { $_.Line }) -join " | " } else { "no errors" })
}
Get-ChildItem $out -Filter *.png | ForEach-Object { $_.Name }
