# Serves the browser build (Builds/web) at http://localhost:8080/ for local checks. Stop with Ctrl+C or by killing it.
# Usage: powershell -ExecutionPolicy Bypass -File Tools/ServeWeb.ps1 [-Port 8080]   (keep this file ASCII-only)
param([int]$Port = 8080)
$root = Join-Path (Split-Path -Parent $PSScriptRoot) "Builds\web"
$types = @{ ".html" = "text/html; charset=utf-8"; ".js" = "application/javascript"; ".css" = "text/css"; ".json" = "application/json";
            ".png" = "image/png"; ".ico" = "image/x-icon"; ".wasm" = "application/wasm" }
$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
"serving $root at http://localhost:$Port/"
while ($listener.IsListening) {
    $ctx = $listener.GetContext()
    $path = [Uri]::UnescapeDataString($ctx.Request.Url.AbsolutePath.TrimStart("/"))
    if ($path -eq "" -or $path.EndsWith("/")) { $path += "index.html" }
    $file = [IO.Path]::GetFullPath((Join-Path $root $path))
    $res = $ctx.Response
    if ($file.StartsWith($root) -and (Test-Path $file -PathType Leaf)) {
        $ext = [IO.Path]::GetExtension($file)
        $res.ContentType = if ($types.ContainsKey($ext)) { $types[$ext] } else { "application/octet-stream" }
        $bytes = [IO.File]::ReadAllBytes($file)
        $res.ContentLength64 = $bytes.Length
        $res.OutputStream.Write($bytes, 0, $bytes.Length)
    } else { $res.StatusCode = 404 }
    $res.Close()
}
