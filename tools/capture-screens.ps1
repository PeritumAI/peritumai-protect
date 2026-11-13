# Captures every main screen of Peritum Protect in the given theme(s) to docs/screenshots.
# Usage (from the repo root, after a Release build):
#   powershell -ExecutionPolicy Bypass -File tools\capture-screens.ps1 -Themes dark
#   powershell -ExecutionPolicy Bypass -File tools\capture-screens.ps1 -Themes light
#   powershell -ExecutionPolicy Bypass -File tools\capture-screens.ps1 -Themes dark -Scale 150
# Pass one theme per run. A comma list via -File becomes one bad theme name.
# -Scale 150 uses the monitor's per-monitor DPI when it is already 150%.
# This script does not change the monitor. If it is not already at that scale, the app
# applies a WPF layout transform (--layout-scale) so the window is the same size it would be at 150%.
# Offline. The app is pointed at a temporary data folder (PERITUM_PROTECT_DATA) that this script deletes.

param(
    [string[]]$Themes = @("light", "dark"),
    [string]$Exe = "",
    [string]$Fixture = "",
    [string]$Language = "",
    [int]$Scale = 100
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Exe) {
    $Exe = Get-ChildItem "$repo\src\Peritum.Protect.App\bin\Release" -Recurse -Filter "Peritum.Protect.exe" |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Exe -or -not (Test-Path $Exe)) { throw "Build the app in Release first, or pass -Exe." }
if (-not $Fixture) { $Fixture = "$repo\fixtures\secrets.txt" }
$outDir = "$repo\docs\screenshots"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$data = Join-Path ([IO.Path]::GetTempPath()) ("peritum-capture-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $data | Out-Null
$env:PERITUM_PROTECT_DATA = $data
$sampleRoot = Join-Path $data "samples"
New-Item -ItemType Directory -Force -Path $sampleRoot | Out-Null
$sampleFixture = Join-Path $sampleRoot (Split-Path $Fixture -Leaf)
Copy-Item $Fixture $sampleFixture -Force
$samplePhish = Join-Path $sampleRoot "link-phish.eml"
Copy-Item (Join-Path $repo "fixtures\link-phish.eml") $samplePhish -Force
$sampleBatch = Join-Path $sampleRoot "batch"
Copy-Item (Join-Path $repo (Join-Path "fixtures" "batch")) $sampleBatch -Recurse -Force
$settings = Join-Path $data "settings.json"

Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class PpCapture {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
  public struct RECT { public int L, T, R, B; }
}
"@

function Stop-App { Get-Process Peritum.Protect -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false; Start-Sleep -Milliseconds 800 }
function Get-Window {
    for ($i = 0; $i -lt 30; $i++) {
        $proc = Get-Process Peritum.Protect -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne 0 } |
            Select-Object -First 1
        if ($proc) { return $proc.MainWindowHandle }
        Start-Sleep -Milliseconds 500
    }
    throw "Peritum Protect window was not found."
}

function Save-Shot([string]$name, $hwnd = $null) {
    Start-Sleep -Milliseconds 900
    if (-not $hwnd) { $hwnd = Get-Window }
    $r = New-Object PpCapture+RECT
    [PpCapture]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $w = $r.R - $r.L
    $h = $r.B - $r.T
    if ($w -lt 20 -or $h -lt 20) { Write-Warning "window too small for $name"; return }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
    [PpCapture]::PrintWindow($hwnd, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc); $g.Dispose()
    $bmp.Save("$outDir\$name.png"); $bmp.Dispose()
    Write-Host "saved $name.png"
}

function Get-AppRoot {
    [System.Windows.Automation.AutomationElement]::FromHandle((Get-Window))
}

function Find-InApp([string]$id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return (Get-AppRoot).FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Find-Global([string]$id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Invoke-Nav([string]$label) {
    $root = Get-AppRoot
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $label)
    $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if (-not $el) {
        $byId = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $label)
        $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $byId)
    }
    if (-not $el) { Write-Warning "nav item not found: $label"; return $false }
    try {
        $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        return $true
    } catch {
        $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
        return $true
    }
}

function Save-SettingsSections([string]$prefix) {
    $scrollEl = Find-InApp "SettingsScroll"
    if (-not $scrollEl) { Write-Warning "settings scroll not found"; return }
    $scroll = $scrollEl.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    $sections = @(
        @{ Id = "settings-protection"; Name = "protection" },
        @{ Id = "settings-sites"; Name = "sites" },
        @{ Id = "settings-checks"; Name = "checks" },
        @{ Id = "settings-country-BA"; Name = "bosnia" },
        @{ Id = "settings-country-BR"; Name = "brazil" },
        @{ Id = "settings-country-HR"; Name = "croatia" },
        @{ Id = "settings-country-FR"; Name = "france" },
        @{ Id = "settings-country-DE"; Name = "germany" },
        @{ Id = "settings-country-IN"; Name = "india" },
        @{ Id = "settings-country-IT"; Name = "italy" },
        @{ Id = "settings-country-ME"; Name = "montenegro" },
        @{ Id = "settings-country-NL"; Name = "netherlands" },
        @{ Id = "settings-country-MK"; Name = "north-macedonia" },
        @{ Id = "settings-country-PL"; Name = "poland" },
        @{ Id = "settings-country-RS"; Name = "serbia" },
        @{ Id = "settings-country-SI"; Name = "slovenia" },
        @{ Id = "settings-country-ES"; Name = "spain" },
        @{ Id = "settings-country-GB"; Name = "united-kingdom" },
        @{ Id = "settings-country-US"; Name = "united-states" },
        @{ Id = "settings-terms"; Name = "terms" },
        @{ Id = "settings-copies"; Name = "copies" },
        @{ Id = "settings-appearance"; Name = "appearance" },
        @{ Id = "settings-startup"; Name = "startup" },
        @{ Id = "settings-explorer"; Name = "explorer" },
        @{ Id = "settings-data"; Name = "data" },
        @{ Id = "settings-language"; Name = "language" },
        @{ Id = "settings-developer"; Name = "developer" }
    )
    try { $scroll.SetScrollPercent(-1, 0) } catch { Write-Warning "settings scroll did not move: $_" }
    Start-Sleep -Milliseconds 150
    foreach ($section in $sections) {
        $landed = $false
        for ($i = 0; $i -lt 30; $i++) {
            $heading = Find-InApp $section.Id
            if (-not $heading) { break }
            $view = $scrollEl.Current.BoundingRectangle
            $rect = $heading.Current.BoundingRectangle
            $visible = $rect.Height -gt 2 -and $rect.Width -gt 2 -and $rect.Top -ge ($view.Top - 2) -and $rect.Bottom -le ($view.Bottom - 8)
            if ($visible -and $rect.Top -le ($view.Top + 90)) { $landed = $true; break }
            $percent = $scroll.Current.VerticalScrollPercent
            $delta = $rect.Top - $view.Top
            if ($rect.Height -le 2) { $delta = 400 }
            $span = [Math]::Max(220, $view.Height)
            $step = [Math]::Max(1.2, [Math]::Min(14, ($delta / $span) * 18))
            if ($delta -lt 0) { $step = -$step }
            if (($step -gt 0 -and $percent -ge 99.5) -or ($step -lt 0 -and $percent -le 0.5)) {
                if ($rect.Top -ge ($view.Top - 2) -and $rect.Top -lt $view.Bottom) { $landed = $true }
                break
            }
            $next = [Math]::Min(100, [Math]::Max(0, $percent + $step))
            $scroll.SetScrollPercent(-1, $next)
            Start-Sleep -Milliseconds 60
        }
        if (-not $landed) { Write-Warning "could not scroll to $($section.Id)" }
        Save-Shot "$prefix-settings-$($section.Name)"
    }
}

function Get-LaunchArgs {
    $monitor = 100
    try { $monitor = [int][math]::Round(100.0 * [PpCapture]::GetDpiForSystem() / 96.0) } catch { $monitor = 100 }
    if ($Scale -eq 100 -or $monitor -eq $Scale) {
        if ($Scale -ne 100) { Write-Host "Monitor DPI is already $Scale%. No layout transform." }
        return @()
    }
    $factor = ($Scale / 100.0).ToString([Globalization.CultureInfo]::InvariantCulture)
    Write-Host "Monitor DPI is $monitor%. The monitor is not changed. Layout scale $factor."
    return @("--layout-scale", $factor)
}

try {
    $launch = Get-LaunchArgs
    foreach ($theme in $Themes) {
        Stop-App
        $o = [pscustomobject]@{ theme = $theme; showFirstRun = $false; uiLanguage = $Language }
        New-Item -ItemType Directory -Force -Path (Split-Path $settings) | Out-Null
        [IO.File]::WriteAllText($settings, ($o | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding $false))

        $prefix = if ([string]::IsNullOrWhiteSpace($Language)) { $theme } else { "$theme-$Language" }
        if ($Scale -ne 100) { $prefix = "$prefix-$Scale" }
        if ($launch.Count -gt 0) { Start-Process $Exe -ArgumentList $launch } else { Start-Process $Exe }
        Start-Sleep -Seconds 3
        Save-Shot "$prefix-check"
        & $Exe --scan $sampleBatch
        Start-Sleep -Seconds 3
        & $Exe --scan $sampleFixture
        Start-Sleep -Seconds 2
        & $Exe --protect $sampleFixture
        Start-Sleep -Seconds 3
        if (Invoke-Nav "Activity") { Save-Shot "$prefix-activity" }
        foreach ($page in "Restore", "Settings", "Help") {
            if (Invoke-Nav $page) { Save-Shot "$prefix-$($page.ToLower())" }
        }
        if (Invoke-Nav "Settings") { Save-SettingsSections $prefix }
        Invoke-Nav "Check" | Out-Null
        & $Exe --scan $sampleFixture
        Start-Sleep -Seconds 3
        Save-Shot "$prefix-result"
        if (Invoke-Nav "Expand Personal data") { Save-Shot "$prefix-result-expanded" }
        & $Exe --scan $samplePhish
        $expanded = $false
        for ($try = 0; $try -lt 12; $try++) {
            Start-Sleep -Milliseconds 500
            if (Invoke-Nav "Expand Links") { $expanded = $true; break }
        }
        Start-Sleep -Milliseconds 700
        if ($expanded) { Save-Shot "$prefix-links" } else { Write-Warning "Links section was not expanded" }
        if (Invoke-Nav "Preview") {
            Invoke-Nav "Next finding" | Out-Null
            Save-Shot "$prefix-result-preview"
        }
        & $Exe --scan $sampleBatch
        Start-Sleep -Seconds 4
        Save-Shot "$prefix-batch"
        & $Exe --show-warning
        Start-Sleep -Seconds 2
        $card = Find-Global "ClipboardWarning"
        if ($card) {
            Save-Shot "$prefix-clipboard" ([IntPtr]$card.Current.NativeWindowHandle)
        } else {
            Write-Warning "clipboard warning card was not found"
        }
        & $Exe --show-link-warning
        Start-Sleep -Seconds 2
        $linkCard = Find-Global "LinkWarning"
        if ($linkCard) {
            Save-Shot "$prefix-link" ([IntPtr]$linkCard.Current.NativeWindowHandle)
        } else {
            Write-Warning "link warning card was not found"
        }
    }
}
finally {
    Stop-App
    Remove-Item Env:PERITUM_PROTECT_DATA -ErrorAction SilentlyContinue
    if (Test-Path $data) { Remove-Item $data -Recurse -Force }
    Write-Host "Temporary data removed. Screenshots in $outDir"
    $stray = @(Get-ChildItem -LiteralPath $repo -File | Where-Object { $_.Name -match '^[0-9]+$' })
    if ($stray.Count -gt 0) {
        throw "The capture run left a stray numbered file in the repo root: $($stray.Name -join ', ')"
    }
}
