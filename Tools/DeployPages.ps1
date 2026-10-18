# Publishes Web/ (the invite relay page) to the gh-pages branch, which GitHub Pages serves at
# https://jiantailangdasen6-rgb.github.io/tavern-tales/ (join.html#CODE opens the game).
# Usage: powershell -ExecutionPolicy Bypass -File Tools/DeployPages.ps1   (keep this file ASCII-only)
$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path $env:TEMP "tavern-pages"
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
git -C $root fetch origin gh-pages 2>$null
$hasBranch = (git -C $root ls-remote --heads origin gh-pages) -ne $null
New-Item -ItemType Directory -Force $work | Out-Null
git -C $work init -q
git -C $work remote add origin (git -C $root remote get-url origin)
if ($hasBranch) { git -C $work fetch -q origin gh-pages; git -C $work checkout -q -b gh-pages FETCH_HEAD }
else { git -C $work checkout -q --orphan gh-pages }
Get-ChildItem $work -Exclude ".git" | Remove-Item -Recurse -Force
Copy-Item (Join-Path $root "Web\*") $work -Recurse
New-Item -ItemType File (Join-Path $work ".nojekyll") -Force | Out-Null
git -C $work add -A
git -C $work -c user.name="$(git -C $root config user.name)" -c user.email="$(git -C $root config user.email)" commit -q -m "Invite relay page" 2>$null
git -C $work push -q origin gh-pages
"published: https://jiantailangdasen6-rgb.github.io/tavern-tales/"
