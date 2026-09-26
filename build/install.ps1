# zEClass deployment for a classroom machine.
#
# Deliberately a script rather than an MSI: school images are often locked down, and an MSI
# needs elevation and a Windows Installer service that may be disabled. This installs per-user
# with no elevation at all, which is the case that actually matters for a whiteboard.
#
# Run as the teacher or a local admin, on the machine that has the panel plugged in.

param(
  [string]$Source = "",
  [string]$InstallDir = "",
  [switch]$SkipShortcuts,
  [switch]$SkipPrecheck,
  [switch]$Force
)

$ErrorActionPreference = "Stop"
$appName = "zEClass"
$exeName = "zEClass.exe"

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok($msg) { Write-Host "    OK  $msg" -ForegroundColor Green }
function Write-Warn2($msg) { Write-Host "    !!  $msg" -ForegroundColor Yellow }
function Write-Err2($msg) { Write-Host "    XX  $msg" -ForegroundColor Red }

# ---------------------------------------------------------------- source

if ([string]::IsNullOrWhiteSpace($Source)) {
  $Source = Join-Path $PSScriptRoot "..\artifacts\win-x64"
}

# Resolve-Path is the Windows PowerShell 5.1 compatible form; the null-conditional operator
# is not available there and this script must run on a stock school image.
$resolved = Resolve-Path -LiteralPath $Source -ErrorAction SilentlyContinue
if ($resolved) {
  $Source = $resolved.Path
}
else {
  Write-Err2 "No build output at '$Source'. Run build\publish.ps1 first."
  exit 1
}

$sourceExe = Join-Path $Source $exeName
if (-not (Test-Path -LiteralPath $sourceExe)) {
  Write-Err2 "$exeName not found in $Source."
  exit 1
}

if ([string]::IsNullOrWhiteSpace($InstallDir)) {
  $InstallDir = Join-Path $env:LOCALAPPDATA "Programs\$appName"
}
$InstallDir = [Environment]::ExpandEnvironmentVariables($InstallDir)

Write-Step "Pre-install checks"

# Running app would block the copy.
$running = Get-Process -Name "zEClass" -ErrorAction SilentlyContinue
if ($running) {
  if (-not $Force) {
    Write-Err2 "zEClass is running (pid $($running.Id -join ', ')). Close it, or re-run with -Force."
    exit 2
  }
  $running | Stop-Process -Force
  Start-Sleep -Seconds 2
  Write-Ok "stopped the running instance"
}

# ---------------------------------------------------------------- digitizer precheck
#
# Worth doing before install rather than after: a panel that Windows does not recognise as a
# digitizer is the single most common cause of "the board does not work", and finding out
# during a lesson is expensive.

if (-not $SkipPrecheck) {
  Write-Step "Digitizer precheck"
  $hidDevices = @(Get-PnpDevice -Class HID -ErrorAction SilentlyContinue |
    Where-Object { $_.FriendlyName -match 'touch|digitiz|pen|stylus' })
  if ($hidDevices.Count -eq 0) {
    Write-Warn2 "No HID touch or pen device detected."
    Write-Warn2 "The board will still install and work with a mouse."
    Write-Warn2 "If the panel is plugged in, check the USB touch cable and the driver."
  }
  else {
    foreach ($d in $hidDevices) { Write-Ok "$($d.FriendlyName) [$($d.Status)]" }
  }

  $touchEnabled = (Get-ItemProperty `
    'HKLM:\SOFTWARE\Microsoft\TabletTip\1.7' -ErrorAction SilentlyContinue).EnableTouch
  if ($touchEnabled -eq 0) {
    Write-Warn2 "Windows touch is switched off. Enable it in Settings > Bluetooth & devices >"
    Write-Warn2 "Pen & Windows Ink > Touch and pen, or zEClass falls back to the stylus path."
  }
  elseif ($touchEnabled -eq 1) {
    Write-Ok "Windows touch is enabled"
  }
}

# ---------------------------------------------------------------- disk

$drive = (Split-Path -Qualifier $InstallDir).TrimEnd(':')
$free = (Get-PSDrive -Name $drive.TrimEnd('\') -ErrorAction SilentlyContinue).Free
$sourceSize = (Get-ChildItem -LiteralPath $Source -Recurse -File |
  Measure-Object -Property Length -Sum).Sum
if ($free -and ($free / 1GB) -lt 1) {
  Write-Warn2 ("only {0:N1} GB free on {1}; the install needs about {2:N0} MB" -f `
    ($free / 1GB), $drive, ($sourceSize / 1MB))
}

# ---------------------------------------------------------------- install

Write-Step "Installing to $InstallDir"
$existing = Test-Path -LiteralPath (Join-Path $InstallDir $exeName)
if ($existing) {
  if (-not $Force) {
    Write-Err2 "an install already exists at $InstallDir. Re-run with -Force to replace it."
    exit 3
  }

  $backup = "$InstallDir.backup-$(Get-Date -Format yyyyMMdd-HHmmss)"
  Write-Ok "backing up to $backup"
  Move-Item -LiteralPath $InstallDir -Destination $backup
}

New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Copy-Item -Path (Join-Path $Source "*") -Destination $InstallDir -Recurse -Force
$fileCount = (Get-ChildItem -LiteralPath $InstallDir -Recurse -File).Count
Write-Ok "copied $fileCount files"

# ---------------------------------------------------------------- shortcuts

if (-not $SkipShortcuts) {
  Write-Step "Creating shortcuts"
  $shell = New-Object -ComObject WScript.Shell
  foreach ($entry in @(
      @{ Name = "$appName"; Path = (Join-Path $InstallDir $exeName); Dir = "Desktop" },
      @{ Name = "$appName"; Path = (Join-Path $InstallDir $exeName); Dir = "StartMenu" })) {
    try {
      $dir = switch ($entry.Dir) {
        "Desktop" { [Environment]::GetFolderPath("Desktop") }
        "StartMenu" { Join-Path ([Environment]::GetFolderPath("StartMenu")) "Programs" }
      }
      $link = $shell.CreateShortcut((Join-Path $dir "$($entry.Name).lnk"))
      $link.TargetPath = $entry.Path
      $link.WorkingDirectory = $InstallDir
      $link.Description = "zEClass Interactive Whiteboard"
      $link.IconLocation = "$entry.Path,0"
      $link.Save()
      Write-Ok "shortcut in $dir"
    }
    catch {
      Write-Warn2 "could not create the $($entry.Dir) shortcut: $($_.Exception.Message)"
    }
  }
  [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
}

# ---------------------------------------------------------------- first-run config

Write-Step "Per-machine defaults"
$configDir = Join-Path $env:LOCALAPPDATA "$appName"
New-Item -ItemType Directory -Path (Join-Path $configDir "boards") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $configDir "calibration") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $configDir "import") -Force | Out-Null
Write-Ok "writable folders under $configDir (no elevation needed at runtime)"

# ---------------------------------------------------------------- verify

Write-Step "Verifying"
$installed = Join-Path $InstallDir $exeName
if (-not (Test-Path -LiteralPath $installed)) {
  Write-Err2 "install verification failed: $installed is missing."
  exit 4
}

$hash = (Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash
Write-Ok "$exeName sha256 $hash"

$process = Start-Process -FilePath $installed -WorkingDirectory $InstallDir -PassThru
Start-Sleep -Seconds 6
if ($process.HasExited) {
  Write-Err2 "the app exited immediately with code $($process.ExitCode)."
  Write-Err2 "check %LOCALAPPDATA%\zEClass\logs\zEClass.log"
  exit 5
}

$log = Join-Path $configDir "logs\zEClass.log"
if (Test-Path $log) {
  $errors = @(Get-Content $log | Select-String 'Dispatcher' -SimpleMatch)
  if ($errors.Count -gt 0) {
    Write-Warn2 "$($errors.Count) startup error(s) in the log:"
    $errors | Select-Object -First 3 | ForEach-Object { Write-Warn2 "    $($_.Line)" }
  }
}

Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
Write-Ok "the app started and was closed again"

Write-Host ""
Write-Host "Installed." -ForegroundColor Green
Write-Host "  Start it from the desktop shortcut."
Write-Host "  On first use: click Align and touch the four crosses so touch lines up with the panel."
Write-Host "  Logs: $log"
