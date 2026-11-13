# Writes PeritumProtect.ico, PeritumProtect.png, and assets/icon/tray-*.ico.
# Build the app first. Offline.
#   powershell -ExecutionPolicy Bypass -File tools\make-icons.ps1
#   powershell -ExecutionPolicy Bypass -File tools\make-icons.ps1 -Dir "C:\some\folder"
param(
    [string]$Dir = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Dir) { $Dir = $repo }
$exe = Get-ChildItem "$repo\src\Peritum.Protect.App\bin" -Recurse -Filter "Peritum.Protect.exe" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $exe) { throw "Build the app first." }
& $exe --write-icons $Dir
if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
