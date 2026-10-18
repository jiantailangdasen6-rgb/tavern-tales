# Publishes Web/ (the invite relay page) and the browser build (Builds/web -> play/) to the gh-pages branch, which
# GitHub Pages serves at https://jiantailangdasen6-rgb.github.io/tavern-tales/ (join.html#CODE, play/#CODE).
# The branch is rewritten from scratch each time (force push), so old ~90 MB builds do not pile up in its history.
# Usage: powershell -ExecutionPolicy Bypass -File Tools/DeployPages.ps1 [-NoGame]   (keep this file ASCII-only)
param([switch]$NoGame)
$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path $env:TEMP "tavern-pages"
$game = Join-Path $root "Builds\web"
if (-not $NoGame -and -not (Test-Path (Join-Path $game "index.html"))) { throw "no browser build in Builds\web (run BuildScript.BuildWeb, or pass -NoGame)" }
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force $work | Out-Null
git -C $work init -q
git -C $work remote add origin (git -C $root remote get-url origin)
git -C $work checkout -q --orphan gh-pages
Copy-Item (Join-Path $root "Web\*") $work -Recurse
if (-not $NoGame) { Copy-Item $game (Join-Path $work "play") -Recurse }
New-Item -ItemType File (Join-Path $work ".nojekyll") -Force | Out-Null
git -C $work add -A
git -C $work -c user.name="$(git -C $root config user.name)" -c user.email="$(git -C $root config user.email)" commit -q -m "Invite page and browser build"
git -C $work push -q -f origin gh-pages
if ($LASTEXITCODE -ne 0) { throw "push failed" }
"published: https://jiantailangdasen6-rgb.github.io/tavern-tales/"
