# Run a Unity editor method in batch mode and print the interesting log lines.
# Usage: powershell -ExecutionPolicy Bypass -File Tools/RunUnity.ps1 -Method Tavern.Unity.Editor.BuildScript.BuildWindows
# Fails if the project is open in the Unity Editor.
param([Parameter(Mandatory = $true)][string]$Method, [string]$Extra = "")

$root = Split-Path -Parent $PSScriptRoot
$unity = "C:\Program Files\Unity\Hub\Editor\6000.6.2f1\Editor\Unity.exe"
$name = ($Method -split '\.')[-1]
$log = Join-Path $root "Logs\unity\$name.log"
New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null

$argList = @("-batchmode", "-nographics", "-quit", "-projectPath", (Join-Path $root "Unity"), "-executeMethod", $Method, "-logFile", $log)
if ($Extra) { $argList += ($Extra -split ' ') }
$p = Start-Process -FilePath $unity -ArgumentList $argList -Wait -PassThru -NoNewWindow
Select-String -Path $log -Pattern "error CS", "\[BuildScript\]", "\[ProjectSetup\]", "Exception" | Select-Object -First 30 | ForEach-Object { $_.Line }
"exit code: $($p.ExitCode)"
exit $p.ExitCode
