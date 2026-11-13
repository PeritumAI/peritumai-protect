<#
.SYNOPSIS
  Builds a PeritumAI Protect release: tests, self-contained x64 publish, portable zip, installer, SHA256SUMS.txt.

.DESCRIPTION
  Used by .github/workflows/release.yml and for a local dry run. Output goes to artifacts\ (git-ignored).
  Windows PowerShell 5.1 and PowerShell 7 both work.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File build\publish-release.ps1 -Version 1.0.0
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [switch]$SkipTests,
    [switch]$SkipInstaller,
    [string]$Iscc = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo
# A stale user DOTNET_ROOT breaks .NET 10 apps on some PCs.
[Environment]::SetEnvironmentVariable("DOTNET_ROOT", $null, "Process")
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

function Invoke-Checked([string]$what, [scriptblock]$block) {
    & $block
    if ($LASTEXITCODE -ne 0) { throw "$what failed with exit code $LASTEXITCODE" }
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3, got '$Version'." }
$project = "src\Peritum.Protect.App\Peritum.Protect.App.csproj"
[xml]$csproj = Get-Content $project
$projectVersion = ($csproj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
if ($projectVersion -ne $Version) { throw "Version $Version does not match <Version>$projectVersion</Version> in $project. Update the csproj first." }
if (-not (Select-String -Path CHANGELOG.md -Pattern ([regex]::Escape("[$Version]")) -Quiet)) { throw "CHANGELOG.md has no [$Version] section." }

$artifacts = Join-Path $repo "artifacts"
$publish = Join-Path $artifacts "publish"
if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }
New-Item -ItemType Directory -Force $publish | Out-Null

if (-not $SkipTests) {
    Invoke-Checked "dotnet test" { dotnet test PeritumProtect.sln -c Release }
}

Invoke-Checked "dotnet publish" {
    dotnet publish $project -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none -p:DebugSymbols=false -o $publish
}

foreach ($required in "Peritum.Protect.exe", "LICENSE", "NOTICE", "THIRD-PARTY-NOTICES.md", "PRIVACY.md", "SECURITY.md", "lang\en.json", "rules\builtin", "PeritumProtect.ico", "tray-on.ico", "tray-paused.ico", "tray-warning.ico") {
    if (-not (Test-Path (Join-Path $publish $required))) { throw "Published folder is missing $required." }
}
if (Test-Path (Join-Path $publish "fixtures")) { throw "Test fixtures must not ship. Check CopyToPublishDirectory in the csproj." }
$exeVersion = (Get-Item (Join-Path $publish "Peritum.Protect.exe")).VersionInfo.ProductVersion
if (-not $exeVersion.StartsWith($Version)) { throw "Peritum.Protect.exe reports version $exeVersion, expected $Version." }

$zip = Join-Path $artifacts "PeritumAI-Protect-$Version-portable-x64.zip"
Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $zip

if (-not $SkipInstaller) {
    if ($Iscc -eq "") {
        $candidates = @(
            (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 7\ISCC.exe"),
            (Join-Path $env:ProgramFiles "Inno Setup 7\ISCC.exe"),
            (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
            (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
            (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 7\ISCC.exe"),
            (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
        )
        $Iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if (-not $Iscc) { throw "Inno Setup (ISCC.exe) was not found. Install it, pass -Iscc <path>, or use -SkipInstaller." }
    Invoke-Checked "ISCC" { & $Iscc "/DAppVersion=$Version" "/DPublishDir=$publish" "installer\PeritumAI-Protect.iss" }
}

# Checksums for everything a user downloads. The publish folder itself is not uploaded.
$sums = Get-ChildItem $artifacts -File | Where-Object { $_.Name -ne "SHA256SUMS.txt" } | Sort-Object Name | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
[IO.File]::WriteAllLines((Join-Path $artifacts "SHA256SUMS.txt"), [string[]]$sums)

Write-Host ""
Write-Host "Release files in $artifacts" -ForegroundColor Green
Get-Content (Join-Path $artifacts "SHA256SUMS.txt")
