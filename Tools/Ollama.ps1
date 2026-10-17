# Start / stop the local Ollama server (zip install in D:\tools\ollama, models in D:\tools\ollama-models).
# Usage: powershell -ExecutionPolicy Bypass -File Tools/Ollama.ps1 [-Stop] [-Pull <model>]
# The models folder is passed to the server process only (no system-wide environment variable).
param([switch]$Stop, [string]$Pull)

$exe = "D:\tools\ollama\ollama.exe"
$models = "D:\tools\ollama-models"
$log = "D:\tools\ollama\serve.log"

if ($Stop) {
    Get-Process ollama -ErrorAction SilentlyContinue | Stop-Process -Force
    "ollama stopped"
    exit 0
}

function Test-Up {
    try { $r = Invoke-WebRequest -Uri "http://127.0.0.1:11434/api/version" -UseBasicParsing -TimeoutSec 3; return $r.StatusCode -eq 200 } catch { return $false }
}

if (-not (Test-Up)) {
    if (-not (Test-Path $exe)) { Write-Error "ollama not found: $exe"; exit 1 }
    New-Item -ItemType Directory -Force $models | Out-Null
    $env:OLLAMA_MODELS = $models
    Start-Process -FilePath $exe -ArgumentList "serve" -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError "$log.err"
    for ($i = 0; $i -lt 30; $i++) { Start-Sleep -Seconds 1; if (Test-Up) { break } }
    if (-not (Test-Up)) { Write-Error "ollama did not start; see $log.err"; exit 1 }
}
"ollama running (http://127.0.0.1:11434, models in $models)"

if ($Pull) {
    & $exe pull $Pull
    exit $LASTEXITCODE
}
