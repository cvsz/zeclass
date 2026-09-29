# zEClass removal for a classroom machine.
#
# The mirror of build\install.ps1: per-user, no elevation, and deliberately conservative.
# It only stops processes running from the install directory it is about to remove, only
# removes that directory and the shortcuts it created, and never touches user data unless
# -PurgeUserData is passed explicitly — a teacher's boards and calibration survive an
# uninstall by default.
#
# Run as the teacher or a local admin.

param(
  [string]$InstallDir = "",
  [switch]$SkipShortcuts,
  [switch]$PurgeUserData,
  [switch]$Backup
)

$ErrorActionPreference = "Stop"
$appName = "zEClass"
$exeName = "zEClass.exe"

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok($msg) { Write-Host "    OK  $msg" -ForegroundColor Green }
function Write-Warn2($msg) { Write-Host "    !!  $msg" -ForegroundColor Yellow }
function Write-Err2($msg) { Write-Host "    XX  $msg" -ForegroundColor Red }

if ([string]::IsNullOrWhiteSpace($InstallDir)) {
  $InstallDir = Join-Path $env:LOCALAPPDATA "Programs\$appName"
}
$InstallDir = [Environment]::ExpandEnvironmentVariables($InstallDir)

Write-Step "Pre-remove checks"

if (-not (Test-Path -LiteralPath $InstallDir)) {
  Write-Err2 "no install at $InstallDir; nothing to remove."
  exit 3
}

# Stop only processes running from this directory. A portable copy, a second install,
# or a renamed binary elsewhere is somebody else's process and must not be killed.
$running = @(Get-Process -Name "zEClass" -ErrorAction SilentlyContinue | Where-Object {
  try { $_.Path -and ($_.Path.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase)) }
  catch { $false }
})
if ($running.Count -gt 0) {
  Write-Step "Stopping running instance(s): pid $($running.Id -join ', ')"
  $running | Stop-Process -Force
  Start-Sleep -Seconds 2
  Write-Ok "stopped"
}

# ---------------------------------------------------------------- shortcuts

if (-not $SkipShortcuts) {
  Write-Step "Removing shortcuts"
  $shell = New-Object -ComObject WScript.Shell
  # Newline-separated rather than comma-continued: Windows PowerShell 5.1 rejects a
  # line break directly after a comma inside a foreach expression.
  $shortcutDirs = @(
    [Environment]::GetFolderPath("Desktop")
    Join-Path ([Environment]::GetFolderPath("StartMenu")) "Programs"
  )
  foreach ($dir in $shortcutDirs) {
    $link = Join-Path $dir "$appName.lnk"
    if (Test-Path -LiteralPath $link) {
      try {
        Remove-Item -LiteralPath $link -Force
        Write-Ok "removed $link"
      }
      catch {
        Write-Warn2 "could not remove ${link}: $($_.Exception.Message)"
      }
    }
  }
  [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
}

# ---------------------------------------------------------------- install dir

Write-Step "Removing $InstallDir"
try {
  if ($Backup) {
    $destination = "$InstallDir.uninstalled-$(Get-Date -Format yyyyMMdd-HHmmss)"
    Move-Item -LiteralPath $InstallDir -Destination $destination
    Write-Ok "moved aside to $destination (delete it when you are sure)"
  }
  else {
    Remove-Item -LiteralPath $InstallDir -Recurse -Force
  }

  if (Test-Path -LiteralPath (Join-Path $InstallDir $exeName)) {
    throw "$exeName still present after removal."
  }

  Write-Ok "application removed"
}
catch {
  Write-Err2 "removal failed: $($_.Exception.Message)"
  Write-Warn2 "close any program holding a file under $InstallDir and re-run."
  exit 4
}

# ---------------------------------------------------------------- user data
#
# Boards, calibration, and recordings live outside the install directory. They are
# never removed unless the operator asks for it in so many words.

$configDir = Join-Path $env:LOCALAPPDATA $appName
if ($PurgeUserData) {
  Write-Step "Purging user data at $configDir"
  try {
    if (Test-Path -LiteralPath $configDir) {
      Remove-Item -LiteralPath $configDir -Recurse -Force
    }
    Write-Ok "user data removed"
  }
  catch {
    Write-Warn2 "could not remove all user data: $($_.Exception.Message)"
    exit 5
  }
}
elseif (Test-Path -LiteralPath $configDir) {
  Write-Ok "kept user data (boards, calibration) at $configDir"
  Write-Ok "pass -PurgeUserData to remove it too"
}

Write-Host ""
Write-Host "Removed." -ForegroundColor Green
exit 0
